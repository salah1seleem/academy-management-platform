using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed record MediaWithdrawalWrite(Guid RequestId,string? Note);
public static class MediaPublication
{
    public static void MapMediaPublication(this WebApplication app)=>app.MapPost("/api/v1/assets/{id:guid}/withdraw",Withdraw)
        .RequireAuthorization(AcademyPermissions.TenantAccess).AddEndpointFilter<CsrfFilter>()
        .WithMetadata(new Academy.Api.Configuration.AcademyFeatureMetadata("media"));

    internal static Task<bool> CanWithdraw(TenantMembership t,HttpContext h,IAuthorizationService auth,FoundationDbContext db)=>CanAction(t,h,auth,db,false);
    internal static Task<bool> CanPublish(TenantMembership t,HttpContext h,IAuthorizationService auth,FoundationDbContext db)=>CanAction(t,h,auth,db,true);
    private static async Task<bool> CanAction(TenantMembership t,HttpContext h,IAuthorizationService auth,FoundationDbContext db,bool add)=>
        t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin&&
        (await auth.AuthorizeAsync(h.User,h,AcademyPermissions.PlayerMediaRead)).Succeeded&&
        (await auth.AuthorizeAsync(h.User,h,AcademyPermissions.PlayerMediaManage)).Succeeded&&
        (t.Role==AcademyRole.AcademyOwner||!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.UseActionGrants)||await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="media"&&g.IsActive&&g.Read&&(add?g.Add:g.Edit)));

    private static async Task<IResult> Withdraw(Guid id,MediaWithdrawalWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,IAuthorizationService auth,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";
        var t=(await tenant.ResolveAsync())!;
        if(!await CanWithdraw(t,h,auth,db))return Results.Forbid();
        if(b.RequestId==Guid.Empty||b.Note?.Length>500)return Results.BadRequest(new{message="أعد فتح إجراء السحب؛ الملاحظة الاختيارية حتى500 حرف."});
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AcademyMemberships\" WHERE \"Id\"={t.MembershipId} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        var live=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==t.MembershipId&&m.UserId==db.RequestUserId&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive&&(!m.PrimaryMembershipId.HasValue||db.AcademyMemberships.Any(p=>p.Id==m.PrimaryMembershipId&&p.IsActive&&p.User.IsActive)));
        if(live==null||live.Role!=t.Role||t.AdditionalContext||!live.AllSports)return Results.Forbid();
        // Reject a stale scope after waiting on grant edits, including on replay.
        var branches=t.Role==AcademyRole.AcademyAdmin&&!live.AllBranches?await db.MembershipBranchAccesses.Where(a=>a.MembershipId==t.MembershipId&&a.IsActive&&db.Branches.Any(b=>b.Id==a.BranchId&&b.IsActive)).Select(a=>a.BranchId).ToArrayAsync():null;
        if(branches!=null&&(!db.BranchScopeRestricted||db.RequestBranchIds.Any(b=>!branches.Contains(b))))return Results.Forbid();
        await GroupMediaPrivacy.Lock(db,t.AcademyId);
        if(!await CanWithdraw(t,h,auth,db))return Results.Forbid();
        var asset=await db.PrivateAssets.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==id&&a.Purpose=="Media");
        if(asset==null)return Results.NotFound(); // Branch and sport filters apply even to the original uploader.
        var note=string.IsNullOrWhiteSpace(b.Note)?null:b.Note.Trim();
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,note}))));
        var prior=await db.PrivateMediaWithdrawals.SingleOrDefaultAsync(w=>w.ActorUserId==db.RequestUserId&&w.RequestId==b.RequestId);
        if(prior!=null)return prior.RequestHash==hash?Results.Ok(new{prior.Id,replay=true}):Conflict();
        if(await db.PrivateMediaWithdrawals.AnyAsync(w=>w.AssetId==id))return Conflict();
        var now=clock.GetUtcNow();var w=new PrivateMediaWithdrawal{Id=Guid.NewGuid(),AcademyId=t.AcademyId,AssetId=id,ActorUserId=db.RequestUserId,RequestId=b.RequestId,RequestHash=hash,Note=note,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.PrivateMediaWithdrawals.Add(w);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{w.Id});
    }
    private static IResult Conflict()=>Results.Conflict(new{message="تغير إجراء السحب أو سُحبت النسخة بالفعل؛ أعد فتح المعرض. إعادة النشر تحتاج رفع نسخة ومراجعة جديدة."});
}
