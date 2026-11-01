using System.Security.Cryptography;
using System.Text;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Adds snapshots inside the caller's business transaction. No network delivery.
public static class GuardianLifecycleNotice
{
    public static async Task Add(FoundationDbContext db,Guid academy,Guid enrollment,string key,Guid source,
        string targetKind,Guid? target,DateTimeOffset now,int? count=null,DateOnly? date=null,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Lifecycle notice requires the business transaction");
        var child=await db.SportEnrollments.Where(e=>e.AcademyId==academy&&e.Id==enrollment&&e.IsActive&&e.Player.IsActive)
            .Select(e=>new{e.PlayerId,e.Player.ArabicName}).SingleOrDefaultAsync(token);
        if(child is null)return;
        var recipients=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==academy&&l.PlayerId==child.PlayerId&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.User.IsActive)
            .Select(l=>new{l.Guardian.UserId,l.Guardian.User.DisplayName}).Distinct().ToArrayAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"lifecycle-notice:{academy}:{key}:{enrollment}:{source}"},0))",token);
        foreach(var guardian in recipients){
            var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"lifecycle-notice/v1/{academy}/{key}/{enrollment}/{source}/{guardian.UserId}"))[..16]);
            if(db.AcademyNotices.Local.Any(n=>n.Id==id)||await db.AcademyNotices.AnyAsync(n=>n.AcademyId==academy&&n.Id==id,token))continue;
            var notice=new AcademyNotice{EventKey=key,Id=id,AcademyId=academy,UserId=guardian.UserId,AudienceRole="Guardian",TargetKind=targetKind,TargetId=target??child.PlayerId,
                Message=await NoticeText.Render(db,academy,key,NoticeEndpoints.Defaults[key],child.ArabicName,guardian:guardian.DisplayName,count:count,date:date),CreatedAtUtc=now,UpdatedAtUtc=now};
            await db.BindCashLifecycleNotice(notice,enrollment,token);
            db.AcademyNotices.Add(notice);
        }
    }
}
