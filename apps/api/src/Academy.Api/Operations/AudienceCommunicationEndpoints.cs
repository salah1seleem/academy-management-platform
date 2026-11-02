using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static partial class AudienceCommunicationEndpoints
{
    public static void MapAudienceCommunication(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/communication").RequireAuthorization(AcademyPermissions.TenantAccess);
        foreach(var kind in new[]{"groups","teams"})
        {
            var group=api.MapGroup("/"+kind+"/{id:guid}");
            group.MapGet("",Read);
            group.MapPut("/whatsapp",SaveLink).AddEndpointFilter<CsrfFilter>();
            group.MapPost("/preview",Preview).AddEndpointFilter<CsrfFilter>();
            group.MapPost("/send",Send).AddEndpointFilter<CsrfFilter>();
        }
        api.MapGet("/announcements/{id:guid}",Announcement);
    }
    private static bool Team(HttpContext c)=>c.Request.Path.Value!.Contains("/teams/",StringComparison.Ordinal);
    private sealed record Target(Guid Id,string Name,bool Team);
    private static IQueryable<Academy.Infrastructure.Structure.TrainingGroup> VisibleGroups(FoundationDbContext db,TenantMembership t) => db.TrainingGroups.Where(g=>db.Branches.Any(b=>b.Id==g.BranchId&&b.IsActive)&&db.Sports.Any(s=>s.Id==g.SportId&&s.IsActive)&&(t.Role!=AcademyRole.AcademyAdmin||db.AcademyMemberships.Any(m=>m.Id==t.MembershipId&&(m.AllBranches||db.MembershipBranchAccesses.Any(a=>a.MembershipId==m.Id&&a.IsActive&&a.BranchId==g.BranchId))&&(m.AllSports||db.MembershipSportAccesses.Any(a=>a.MembershipId==m.Id&&a.IsActive&&a.SportId==g.SportId)))));
    private static IQueryable<MatchTeam> VisibleTeams(FoundationDbContext db,TenantMembership t) => db.MatchTeams.Where(g=>db.Branches.Any(b=>b.Id==g.BranchId&&b.IsActive)&&db.Sports.Any(s=>s.Id==g.SportId&&s.IsActive)&&(t.Role!=AcademyRole.AcademyAdmin||db.AcademyMemberships.Any(m=>m.Id==t.MembershipId&&(m.AllBranches||db.MembershipBranchAccesses.Any(a=>a.MembershipId==m.Id&&a.IsActive&&a.BranchId==g.BranchId))&&(m.AllSports||db.MembershipSportAccesses.Any(a=>a.MembershipId==m.Id&&a.IsActive&&a.SportId==g.SportId)))));
    private static async Task<Target?> TargetFor(Guid id,bool team,TenantMembership t,FoundationDbContext db,TimeProvider clock,AcademyConfigurationService config)
    {
        if(!await config.Enabled(t.AcademyId,team?"matches":"training"))return null;
        if(!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.AcademyId==t.AcademyId&&m.IsActive&&m.User.IsActive))return null;
        if(team) return await VisibleTeams(db,t).Where(g=>g.AcademyId==t.AcademyId&&g.Id==id&&g.IsActive&&(t.Role!=AcademyRole.Coach||g.CoachMembershipId==t.MembershipId)).Select(g=>new Target(g.Id,g.Name,true)).SingleOrDefaultAsync();
        return await VisibleGroups(db,t).Where(g=>g.AcademyId==t.AcademyId&&g.Id==id&&g.IsActive&&(t.Role!=AcademyRole.Coach||db.StaffGroupAssignments.Any(a=>a.AcademyId==t.AcademyId&&a.IsActive&&a.AcademyMembershipId==t.MembershipId&&a.TrainingGroupId==g.Id))).Select(g=>new Target(g.Id,g.ArabicName,false)).SingleOrDefaultAsync();
    }
    private static async Task<bool> Staff(TenantMembership t,HttpContext c,IAuthorizationService auth,FoundationDbContext db,bool write=false)
    {
        if(t.Role is not(AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach))return false;
        if(t.Role==AcademyRole.Coach){
            if(!write)return true;
            if(!Guid.TryParse(c.Request.RouteValues["id"]?.ToString(),out var id))return false;
            return await db.AudienceCommunications.AnyAsync(g=>g.AcademyId==t.AcademyId&&g.IsActive&&g.CoachCanSend&&(Team(c)?g.TeamId==id:g.TrainingGroupId==id));
        }
        if(!(await auth.AuthorizeAsync(c.User,c,AcademyPermissions.StructureManage)).Succeeded)return false;
        if(!write||t.Role==AcademyRole.AcademyOwner)return true;
        var action=await db.AcademyMemberships.Where(m=>m.Id==t.MembershipId).Select(m=>m.UseActionGrants).SingleAsync();
        if(!action)return true;
        var module=Team(c)?"matches":"academy";
        return await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module==module&&g.Read&&g.Edit)
            &&await db.StaffGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Allowed&&g.Permission==(Team(c)?"workflow.teams.communicate":"workflow.groups.communicate"));
    }
    private sealed record Audience(Guid[] Players,Guid[] Users);
    private static async Task<Audience?> Recipients(Target target,Guid? matchId,TenantMembership t,FoundationDbContext db,TimeProvider clock)
    {
        var day=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);
        Guid[] players;
        if(matchId.HasValue)
        {
            if(!target.Team||!await db.AcademyMatches.AnyAsync(m=>m.AcademyId==t.AcademyId&&m.Id==matchId&&m.TeamId==target.Id&&m.IsActive&&m.PublishedAtUtc!=null&&(t.Role!=AcademyRole.Coach||db.MatchCoachAssignments.Where(a=>a.MatchId==m.Id&&a.IsActive).OrderByDescending(a=>a.Sequence).Select(a=>a.CoachMembershipId).FirstOrDefault()==t.MembershipId)))return null;
            players=await db.MatchParticipants.Where(p=>p.AcademyId==t.AcademyId&&p.MatchId==matchId&&p.IsActive&&db.Players.Any(x=>x.Id==p.PlayerId&&x.IsActive)).Select(p=>p.PlayerId).Distinct().Order().ToArrayAsync();
        }
        else
        {
            var enrollments=db.SportEnrollments.Where(e=>e.AcademyId==t.AcademyId&&e.IsActive&&e.Player.IsActive);
            enrollments=target.Team?enrollments.Where(e=>db.MatchTeamPlayers.Any(p=>p.AcademyId==t.AcademyId&&p.TeamId==target.Id&&p.EnrollmentId==e.Id&&p.IsActive))
                :enrollments.Where(e=>FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,day)==target.Id);
            players=await enrollments.Select(e=>e.PlayerId).Distinct().Order().ToArrayAsync();
        }
        var users=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==t.AcademyId&&l.IsActive&&players.Contains(l.PlayerId)&&l.Guardian.IsActive&&l.Guardian.User.IsActive&&db.AcademyMemberships.Any(m=>m.AcademyId==t.AcademyId&&m.UserId==l.Guardian.UserId&&m.Role==AcademyRole.Guardian&&m.IsActive)).Select(l=>l.Guardian.UserId).Distinct().Order().ToArrayAsync();
        return new(players,users);
    }
    private static IQueryable<AudienceCommunication> Link(FoundationDbContext db,TenantMembership t,Target target)=>db.AudienceCommunications.Where(l=>l.AcademyId==t.AcademyId&&(target.Team?l.TeamId==target.Id:l.TrainingGroupId==target.Id));
    [GeneratedRegex("^https://chat\\.whatsapp\\.com/[A-Za-z0-9]{20,32}$",RegexOptions.CultureInvariant)]private static partial Regex InvitePattern();
    public static string? ValidInvite(string? input)=>string.IsNullOrWhiteSpace(input)?null:InvitePattern().IsMatch(input.Trim())?input.Trim():null;
    private static async Task<IResult> Read(Guid id,CurrentTenant tenant,HttpContext c,IAuthorizationService auth,FoundationDbContext db,TimeProvider clock,AcademyConfigurationService config)
    {
        var t=(await tenant.ResolveAsync())!;if(!await Staff(t,c,auth,db))return Results.Forbid();var target=await TargetFor(id,Team(c),t,db,clock,config);if(target is null)return Results.NotFound();
        c.Response.Headers.CacheControl="private, no-store";var audience=(await Recipients(target,null,t,db,clock))!;var link=await Link(db,t,target).AsNoTracking().SingleOrDefaultAsync();
        object? nextEvent=null;var day=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);
        if(target.Team)nextEvent=await db.AcademyMatches.Where(m=>m.TeamId==id&&m.IsActive&&m.PublishedAtUtc!=null&&m.MatchDate>=day).OrderBy(m=>m.MatchDate).ThenBy(m=>m.StartTime).Select(m=>new{m.Id,date=m.MatchDate,time=m.StartTime,venue=m.Venue,fee=db.AcademyTournaments.Where(x=>x.Id==m.TournamentId&&x.IsActive).Select(x=>(decimal?)x.Fee).SingleOrDefault(),currency=db.AcademyTournaments.Where(x=>x.Id==m.TournamentId).Select(x=>x.Currency).SingleOrDefault()}).FirstOrDefaultAsync();
        else nextEvent=await db.TrainingSessions.Where(s=>s.TrainingGroupId==id&&s.IsActive&&s.SessionDate>=day&&s.Status==Academy.Infrastructure.Attendance.TrainingSessionStatus.Scheduled).OrderBy(s=>s.SessionDate).ThenBy(s=>s.StartTime).Select(s=>new{s.Id,date=s.SessionDate,time=s.StartTime}).FirstOrDefaultAsync();
        return Results.Ok(new{name=target.Name,kind=target.Team?"Team":"Group",playerCount=audience.Players.Length,recipientCount=audience.Users.Length,canSend=await Staff(t,c,auth,db,true),canManageLink=t.Role!=AcademyRole.Coach&&await Staff(t,c,auth,db,true),inviteUrl=link?.InviteUrl,coachCanSend=link?.CoachCanSend??false,canSetCoachPolicy=t.Role==AcademyRole.AcademyOwner,version=link?.Version??0,groupName=t.AcademyName+" · "+target.Name,invitationText="ندعوكم لمجموعة أولياء الأمور في "+t.AcademyName+" · "+target.Name+". الانضمام اختياري من رابط الدعوة.",nextEvent,delivery="StoredInApp",externalPush=false});
    }
    public sealed record LinkWrite(string? InviteUrl,uint ExpectedVersion,bool? CoachCanSend=null);
    private static async Task<IResult> SaveLink(Guid id,LinkWrite body,CurrentTenant tenant,HttpContext c,IAuthorizationService auth,FoundationDbContext db,TimeProvider clock,AcademyConfigurationService config)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role==AcademyRole.Coach||!await Staff(t,c,auth,db,true))return Results.Forbid();
        var url=ValidInvite(body.InviteUrl);if(!string.IsNullOrWhiteSpace(body.InviteUrl)&&url is null)return Results.UnprocessableEntity(new{message="استخدم رابط دعوة المجموعة بصيغة https://chat.whatsapp.com/ ثم رمز الدعوة فقط، دون إضافات."});
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"audience-link:{t.AcademyId}:{id}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        var target=await TargetFor(id,Team(c),t,db,clock,config);if(target is null)return Results.NotFound();if(!await Staff(t,c,auth,db,true))return Results.Forbid();var link=await Link(db,t,target).SingleOrDefaultAsync();if(body.CoachCanSend.HasValue&&t.Role!=AcademyRole.AcademyOwner&&body.CoachCanSend!=(link?.CoachCanSend??false))return Results.Forbid();if((link?.Version??0)!=body.ExpectedVersion)return Results.Conflict(new{message="تغير رابط الدعوة؛ حدّث الصفحة قبل الحفظ."});
        if(link is null){link=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,TrainingGroupId=target.Team?null:id,TeamId=target.Team?id:null,CreatedAtUtc=clock.GetUtcNow()};db.AudienceCommunications.Add(link);}
        if(body.CoachCanSend.HasValue)link.CoachCanSend=body.CoachCanSend.Value;link.InviteUrl=url;link.UpdatedByMembershipId=t.MembershipId;link.UpdatedAtUtc=clock.GetUtcNow();db.AudienceLinkAudits.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,CommunicationId=link.Id,ActorMembershipId=t.MembershipId,Linked=url!=null,CoachCanSend=link.CoachCanSend,CreatedAtUtc=clock.GetUtcNow(),UpdatedAtUtc=clock.GetUtcNow()});await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{version=link.Version,linked=url!=null,coachCanSend=link.CoachCanSend});
    }
    public sealed record AnnouncementWrite(string Message,Guid RequestId,string? PreviewHash=null,bool IncludeInvitation=false,Guid? MatchId=null);
    private static Task<IResult> Preview(Guid id,AnnouncementWrite body,CurrentTenant tenant,HttpContext c,IAuthorizationService auth,FoundationDbContext db,TimeProvider clock,AcademyConfigurationService config)=>Dispatch(id,body,false,tenant,c,auth,db,clock,config);
    private static Task<IResult> Send(Guid id,AnnouncementWrite body,CurrentTenant tenant,HttpContext c,IAuthorizationService auth,FoundationDbContext db,TimeProvider clock,AcademyConfigurationService config)=>Dispatch(id,body,true,tenant,c,auth,db,clock,config);
    private static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static async Task<IResult> Dispatch(Guid id,AnnouncementWrite body,bool send,CurrentTenant tenant,HttpContext c,IAuthorizationService auth,FoundationDbContext db,TimeProvider clock,AcademyConfigurationService config)
    {
        var t=(await tenant.ResolveAsync())!;if(!await Staff(t,c,auth,db,true))return Results.Forbid();
        if(string.IsNullOrWhiteSpace(body.Message)||body.Message.Length>1000||body.RequestId==Guid.Empty)return Results.UnprocessableEntity(new{message="اكتب رسالة عامة من حرف إلى1000حرف."});
        // Invitation URLs are served only from the reauthorized detail, never the persistent inbox text.
        if(body.Message.Contains("chat.whatsapp.com",StringComparison.OrdinalIgnoreCase))return Results.UnprocessableEntity(new{message="فعّل إرفاق الدعوة بدل نسخ رابطها في النص، لحماية الوصول عند تغير عضوية المجموعة."});
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"notice-dispatch:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"audience-link:{t.AcademyId}:{id}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AcademyMemberships\" WHERE \"Id\"={t.MembershipId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        config.DiscardRequestCache(t.AcademyId);var target=await TargetFor(id,Team(c),t,db,clock,config);if(target is null)return Results.NotFound();if(!await Staff(t,c,auth,db,true))return Results.Forbid();
        var requestHash=Hash(new{id,target.Team,message=body.Message.Trim(),body.IncludeInvitation,body.MatchId});var prior=await db.NoticeDispatches.SingleOrDefaultAsync(n=>n.AcademyId==t.AcademyId&&n.ActorMembershipId==t.MembershipId&&n.RequestId==body.RequestId);
        if(prior!=null)return prior.RequestHash==requestHash?Results.Ok(new{sent=prior.RecipientCount,replay=true,delivery="StoredInApp",externalPush=false}):Results.Conflict(new{message="مرجع الإرسال مستخدم لرسالة مختلفة."});
        var audience=await Recipients(target,body.MatchId,t,db,clock);if(audience is null)return Results.UnprocessableEntity(new{message="اختر حدثًا منشورًا ينتمي لهذا الفريق."});
        var link=body.IncludeInvitation?await Link(db,t,target).AsNoTracking().SingleOrDefaultAsync():null;if(body.IncludeInvitation&&link?.InviteUrl is null)return Results.Conflict(new{message="اربط دعوة المجموعة أولًا أو أزل إرفاق الدعوة."});
        var previewHash=Hash(new{requestHash,audience,link=link?.InviteUrl});
        if(!send)return Results.Ok(new{previewHash,playerCount=audience.Players.Length,recipientCount=audience.Users.Length,name=target.Name,message=body.Message.Trim(),includesInvitation=body.IncludeInvitation,delivery="StoredInApp",externalPush=false});
        if(audience.Users.Length==0)return Results.Conflict(new{message="لا توجد حسابات أولياء أمور مرتبطة متاحة لاستقبال الإشعار."});
        if(body.PreviewHash!=previewHash)return Results.Conflict(new{message="تغيرت الرسالة أو جمهور المجموعة أو الدعوة؛ أعد المعاينة قبل الإرسال."});
        var now=clock.GetUtcNow();var announcement=new AudienceAnnouncement{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TrainingGroupId=target.Team?null:id,TeamId=target.Team?id:null,MatchId=body.MatchId,ActorMembershipId=t.MembershipId,RequestId=body.RequestId,Message=body.Message.Trim(),IncludeInvitation=body.IncludeInvitation,PlayerCount=audience.Players.Length,RecipientCount=audience.Users.Length,CreatedAtUtc=now,UpdatedAtUtc=now};db.AudienceAnnouncements.Add(announcement);
        foreach(var user in audience.Users){var notice=new AcademyNotice{Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=user,AudienceRole="Guardian",EventKey="GroupAnnouncement",TargetKind="GroupAnnouncement",TargetId=announcement.Id,Message=target.Name+": "+body.Message.Trim()+(body.IncludeInvitation?" · دعوة المجموعة متاحة داخل التفاصيل.":""),CreatedAtUtc=now,UpdatedAtUtc=now};db.AcademyNotices.Add(notice);await MobilePushEndpoints.Queue(db,notice,now);}
        db.NoticeDispatches.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,ActorMembershipId=t.MembershipId,RequestId=body.RequestId,RequestHash=requestHash,Kind="GroupAnnouncement",RecipientCount=audience.Users.Length,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{id=announcement.Id,sent=audience.Users.Length,replay=false,delivery="StoredInApp",externalPush=false});
    }
    private static async Task<IResult> Announcement(Guid id,CurrentTenant tenant,HttpContext c,IAuthorizationService auth,FoundationDbContext db,TimeProvider clock,AcademyConfigurationService config)
    {
        var t=(await tenant.ResolveAsync())!;var row=await db.AudienceAnnouncements.AsNoTracking().SingleOrDefaultAsync(a=>a.AcademyId==t.AcademyId&&a.Id==id&&a.IsActive);if(row is null)return Results.NotFound();
        var target=await TargetFor(row.TeamId??row.TrainingGroupId!.Value,row.TeamId!=null,t,db,clock,config);if(target is null)return Results.NotFound();
        if(t.Role==AcademyRole.Guardian){var audience=await Recipients(target,row.MatchId,t,db,clock);if(audience is null||!audience.Users.Contains(db.RequestUserId)||!await NoticeEndpoints.OwnNotices(db,t.AcademyId,db.RequestUserId,t.Role).AnyAsync(n=>n.TargetKind=="GroupAnnouncement"&&n.TargetId==id))return Results.NotFound();}
        else if(!await Staff(t,c,auth,db))return Results.Forbid();
        var zone=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync();
        var local=TimeZoneInfo.ConvertTime(row.CreatedAtUtc,TimeZoneInfo.FindSystemTimeZoneById(zone));
        var invite=row.IncludeInvitation?(await Link(db,t,target).AsNoTracking().SingleOrDefaultAsync())?.InviteUrl:null;c.Response.Headers.CacheControl="private, no-store";
        return Results.Ok(new{row.Id,name=target.Name,row.Message,row.CreatedAtUtc,sentOn=local.ToString("yyyy-MM-dd"),sentTime=local.ToString("HH:mm"),inviteUrl=invite,delivery="StoredInApp",externalPush=false,recipientCount=t.Role==AcademyRole.Guardian?(int?)null:row.RecipientCount});
    }
    internal static async Task<bool> GuardianCanRead(FoundationDbContext db,Guid academy,Guid user,Guid id,TimeProvider clock,AcademyConfigurationService config)
    {
        var membership=await db.AcademyMemberships.Where(m=>m.AcademyId==academy&&m.UserId==user&&m.Role==AcademyRole.Guardian&&m.IsActive&&m.User.IsActive).Select(m=>(Guid?)m.Id).FirstOrDefaultAsync();
        if(!membership.HasValue)return false;
        var row=await db.AudienceAnnouncements.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==id&&a.AcademyId==academy&&a.IsActive);if(row is null)return false;
        var t=new TenantMembership(academy,"",AcademyRole.Guardian,membership.Value);
        var target=await TargetFor(row.TeamId??row.TrainingGroupId!.Value,row.TeamId!=null,t,db,clock,config);if(target is null)return false;
        var audience=await Recipients(target,row.MatchId,t,db,clock);return audience?.Users.Contains(user)==true;
    }
}
