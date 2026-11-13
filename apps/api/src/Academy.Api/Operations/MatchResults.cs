using System.Text.Json;
using Academy.Api.Importing;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public sealed record MatchGoalInput(int Ordinal,string Kind,Guid? PlayerId=null);
public static class MatchResults
{
    public sealed record State(int? Our,int? Opponent,bool Finalized,MatchGoalInput[] Goals);
    public static async Task<State> Capture(FoundationDbContext db,AcademyMatch row)=>new(row.OurScore,row.OpponentScore,row.ResultFinalized,
        await db.MatchGoalSlots.Where(g=>g.MatchId==row.Id).OrderBy(g=>g.Ordinal).Select(g=>new MatchGoalInput(g.Ordinal,g.Kind,g.PlayerId)).ToArrayAsync());
    public static string GoalWords(int count)=>count switch{1=>"هدفًا",2=>"هدفين",>=3 and <=10=>$"{count} أهداف",_=>$"{count} هدفًا"};
    public static async Task<string?> Apply(FoundationDbContext db,AcademyMatch row,MatchTeam team,MatchWrite body,Guid actor,IReadOnlyList<Guid> roster,State before,DateTimeOffset now)
    {
        if(body.ResultVersion.HasValue&&body.ResultVersion!=row.ResultRevision)return "تغيرت النتيجة؛ حدّث المباراة وراجع الهدافين قبل الحفظ.";
        var football=await db.Sports.Where(s=>s.Id==team.SportId&&s.AcademyId==row.AcademyId).Select(s=>s.Kind==SportKind.Football).SingleAsync();
        var finalized=body.ResultFinalized??row.ResultFinalized;
        finalized=finalized&&body.IsActive;
        if(finalized&&(!body.OurScore.HasValue||!body.OpponentScore.HasValue||!body.Publish&&row.PublishedAtUtc==null))return "اعتماد النتيجة يحتاج نشر المباراة ونتيجتي الفريقين.";
        if(body.GoalSlots?.Length>100||!football&&body.GoalSlots?.Length>0)return "خانات الهدافين تخص كرة القدم فقط، وبعدد أهداف فريقنا.";
        MatchGoalInput[] goals=[];
        if(football&&body.OurScore.HasValue) {
            goals=body.GoalSlots??Enumerable.Range(1,body.OurScore.Value).Select(n=>before.Goals.FirstOrDefault(g=>g.Ordinal==n)??new MatchGoalInput(n,"Unknown")).ToArray();
            if(goals.Length!=body.OurScore||!goals.Select(g=>g.Ordinal).Order().SequenceEqual(Enumerable.Range(1,body.OurScore.Value)))return "عدد خانات أهداف فريقنا لا يطابق النتيجة. راجع الأهداف المضافة أو المحذوفة.";
            if(goals.Any(g=>g.Kind is not("Player" or "Unknown" or "OwnGoal")||g.Kind=="Player"&&(!g.PlayerId.HasValue||!roster.Contains(g.PlayerId.Value))||g.Kind!="Player"&&g.PlayerId.HasValue))return "اختر الهداف من قائمة هذه المباراة، أو اختر غير معروف أو هدفًا عكسيًا صراحةً.";
            goals=goals.OrderBy(g=>g.Ordinal).ToArray();
        } else if(body.GoalSlots?.Length>0)return "أدخل نتيجة فريقنا قبل الهدافين.";
        var after=new State(body.OurScore,body.OpponentScore,finalized,goals);
        var beforeJson=JsonSerializer.Serialize(before);var afterJson=JsonSerializer.Serialize(after);row.ResultFingerprint=ImportCommitService.Hash(after);
        if(beforeJson==afterJson){if(row.Opponent!=body.Opponent.Trim()||row.MatchDate!=body.MatchDate)await ReconcileNotices(db,row,team,body,goals,false,now);return null;}
        var existing=await db.MatchGoalSlots.Where(g=>g.MatchId==row.Id).ToListAsync();
        foreach(var old in existing)if(!goals.Any(g=>g.Ordinal==old.Ordinal))db.MatchGoalSlots.Remove(old);
        foreach(var goal in goals){var entry=existing.SingleOrDefault(g=>g.Ordinal==goal.Ordinal);if(entry is null){entry=new(){Id=Guid.NewGuid(),AcademyId=row.AcademyId,MatchId=row.Id,Ordinal=goal.Ordinal,CreatedAtUtc=now};db.MatchGoalSlots.Add(entry);}entry.Kind=goal.Kind;entry.PlayerId=goal.PlayerId;entry.UpdatedAtUtc=now;}
        var firstPublication=row.ResultPublishedAtUtc is null&&finalized;
        row.ResultFinalized=finalized;if(finalized)row.ResultPublishedAtUtc??=now;row.ResultRevision++;
        db.MatchResultChanges.Add(new(){Id=Guid.NewGuid(),AcademyId=row.AcademyId,MatchId=row.Id,ActorMembershipId=actor,Revision=row.ResultRevision,Before=beforeJson,After=afterJson,CreatedAtUtc=now,UpdatedAtUtc=now});
        await ReconcileNotices(db,row,team,body,goals,firstPublication,now);return null;
    }
    private static async Task ReconcileNotices(FoundationDbContext db,AcademyMatch match,MatchTeam team,MatchWrite body,MatchGoalInput[] goals,bool first,DateTimeOffset now)
    {
        var credited=match.ResultFinalized?goals.Where(g=>g.Kind=="Player").GroupBy(g=>g.PlayerId!.Value).ToDictionary(g=>g.Key,g=>g.Count()):new Dictionary<Guid,int>();
        var disabled=await db.NoticeTemplates.AnyAsync(t=>t.AcademyId==match.AcademyId&&t.Key=="Match"&&!t.IsActive);
        var prior=await db.AcademyNotices.Where(n=>n.AcademyId==match.AcademyId&&n.MatchId==match.Id&&n.EventKey=="MatchGoals"&&n.SubjectPlayerId!=null).ToListAsync();
        var playerIds=credited.Keys.Concat(prior.Select(n=>n.SubjectPlayerId!.Value)).Distinct().ToArray();
        if(playerIds.Length==0)return;
        var players=await db.Players.Where(p=>playerIds.Contains(p.Id)).ToDictionaryAsync(p=>p.Id,p=>p.ArabicName);
        var recipients=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==match.AcademyId&&playerIds.Contains(l.PlayerId)&&l.IsActive&&l.Player.IsActive&&l.Guardian.IsActive&&l.Guardian.User.IsActive&&db.AcademyMemberships.Any(m=>m.AcademyId==match.AcademyId&&m.UserId==l.Guardian.UserId&&m.Role==AcademyRole.Guardian&&m.IsActive)).Select(l=>new{l.PlayerId,l.Guardian.UserId}).Distinct().ToArrayAsync();
        foreach(var notice in prior.Where(n=>!recipients.Any(r=>r.PlayerId==n.SubjectPlayerId&&r.UserId==n.UserId))){notice.IsActive=false;notice.UpdatedAtUtc=now;}
        foreach(var recipient in recipients) {
            var count=credited.GetValueOrDefault(recipient.PlayerId);var notice=prior.SingleOrDefault(n=>n.SubjectPlayerId==recipient.PlayerId&&n.UserId==recipient.UserId);
            if(notice is null&&count==0)continue;
            var correction=notice!=null||!first;
            var message=!body.IsActive?"أُلغيت المباراة؛ لا تُعتمد نتيجتها السابقة.":!match.ResultFinalized?"نتيجة المباراة قيد المراجعة؛ راجعوا الملخص بعد اعتماد التصحيح.":count==0?$"تصحيح نتيجة: لم تُسجّل أهداف معتمدة باسم {players.GetValueOrDefault(recipient.PlayerId,"اللاعب")} في هذه المباراة.":$"{(correction?"تصحيح نتيجة: ":"مبروك! ")}سجّل {players.GetValueOrDefault(recipient.PlayerId,"اللاعب")} {GoalWords(count)} في مباراة {body.MatchDate:yyyy-MM-dd}.";
            if (body.IsActive && match.ResultFinalized)
                message += $" النتيجة: {team.Name} {body.OurScore} – {body.Opponent.Trim()} {body.OpponentScore}.";
            if(notice is null){notice=new(){Id=Guid.NewGuid(),AcademyId=match.AcademyId,UserId=recipient.UserId,SubjectPlayerId=recipient.PlayerId,MatchId=match.Id,EventKey="MatchGoals",AudienceRole="Guardian",TargetKind="Matches",TargetId=match.Id,DeduplicationKey=ImportCommitService.Hash(new{kind="MatchGoals",match.Id,player=recipient.PlayerId}),Message=message,CreatedAtUtc=now};db.AcademyNotices.Add(notice);}
            else if(notice.Message!=message){notice.Message=message;notice.IsRead=false;}
            notice.IsActive=!disabled;notice.UpdatedAtUtc=now;
            if(first&&!disabled&&count>0)await MobilePushEndpoints.Queue(db,notice,now);
            // External delivery is queued only for a first finalized summary;
            // corrections retain this stable inbox identity without re-broadcast.
        }
    }
}
