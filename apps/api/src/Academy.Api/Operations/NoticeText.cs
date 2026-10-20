using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class NoticeText
{
    // An edited template changes future messages only; published notices remain snapshots.
    public static async Task<string> Render(FoundationDbContext db,Guid academy,string key,string fallback,string child="",string outcome="",string reason="")
    {
        var template=await db.NoticeTemplates.Where(t=>t.AcademyId==academy&&t.Key==key&&t.IsActive).Select(t=>t.Message).SingleOrDefaultAsync();
        return template==null?fallback:template.Replace("{children}",child).Replace("{outcome}",outcome).Replace("{reason}",reason);
    }

    // Called inside the proof transaction. A repeated upload updates the proof,
    // not a second notification. Only owners receive this financial queue link.
    public static async Task PaymentProof(FoundationDbContext db,Guid academy,Guid request,string kind,DateTimeOffset now)
    {
        var owners=await db.AcademyMemberships.Where(m=>m.AcademyId==academy&&m.IsActive&&m.Role==AcademyRole.AcademyOwner&&m.User.IsActive).Select(m=>m.UserId).Distinct().ToArrayAsync();
        foreach(var user in owners){
            var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"proof-notice/v1/{academy}/{request}/{user}"))[..16]);
            if(db.AcademyNotices.Local.Any(n=>n.Id==id)||await db.AcademyNotices.AnyAsync(n=>n.Id==id))continue;
            db.AcademyNotices.Add(new(){Id=id,AcademyId=academy,UserId=user,AudienceRole="AcademyOwner",TargetKind="PaymentQueue",TargetId=request,
                Message=await Render(db,academy,"Payment","وصل إثبات دفع جديد؛ راجع الطلب والإيصال قبل القبول.")+"\n"+kind,CreatedAtUtc=now,UpdatedAtUtc=now});
        }
    }
}
