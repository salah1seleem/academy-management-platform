using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice4;
using Academy.Api.Slice6;
using Academy.Api.Slice8;
using Academy.Api.Slice5;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Platform;

// A single audited read projection. No target-user authentication, preview token,
// session, navigation proxy, or write endpoint is issued to the operator.
public static class PlatformRolePreview
{
    public sealed record PreviewRequest(Guid RequestId,Guid MembershipId);
    public static void MapPlatformRolePreview(this WebApplication app) => app.MapPost("/api/v1/platform/academies/{academy:guid}/role-preview",Read)
        .RequireAuthorization(PlatformIdentity.Policy).AddEndpointFilter<CsrfFilter>();
    private static object? Value(IResult result) => (result as IValueHttpResult)?.Value;
    private static async Task<IResult> Read(Guid academy,PreviewRequest body,ClaimsPrincipal actor,FoundationDbContext db,TimeProvider time,HttpResponse response,ISubscriptionClock clock,IEvaluationReportCalculator calculator,AcademyConfigurationService configuration)
    {
        if(body.RequestId==Guid.Empty||body.MembershipId==Guid.Empty)return Results.UnprocessableEntity(new{title="اختر عضوية فعلية لمعاينة الدور."});
        var member=await db.AcademyMemberships.AsNoTracking().Include(m=>m.User).Include(m=>m.Academy).SingleOrDefaultAsync(m=>m.Id==body.MembershipId&&m.AcademyId==academy&&m.IsActive&&m.User.IsActive&&(m.PrimaryMembershipId==null||m.PrimaryMembership!.IsActive));
        if(member is null)return Results.NotFound();
        var operatorId=Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"role-preview:"+body.RequestId},0))");
        var audit=await db.PlatformAudits.SingleOrDefaultAsync(a=>a.Id==body.RequestId);
        if(audit!=null&&(audit.ActorUserId!=operatorId||audit.TargetAcademyId!=academy||audit.Action!="academy.role-preview"||JsonDocument.Parse(audit.Details).RootElement.GetProperty("membershipId").GetGuid()!=member.Id))return Results.Conflict(new{title="طلب المعاينة مستخدم لسياق آخر."});
        var t=new TenantMembership(academy,member.Academy.ArabicName,member.Role,member.Id,member.PrimaryMembershipId!=null);
        var branches=member.Role==AcademyRole.AcademyAdmin&&!member.AllBranches?await db.MembershipBranchAccesses.AsNoTracking().Where(a=>a.AcademyId==academy&&a.MembershipId==member.Id&&a.IsActive).Select(a=>a.BranchId).ToArrayAsync():null;
        var sports=await MembershipScope.Sports(db,t);
        // Bind the inspected resource scope to the actual selected membership;
        // HttpContext.User remains the real MFA platform actor throughout.
        db.BindRequestScope(academy,member.UserId,member.Id,branches,sports,additionalContext:t.AdditionalContext);
        response.Headers.CacheControl="private, no-store";
        object? subscriptions=null,guardian=null,nextSession=null;string? unavailable=null;
        var trainingEnabled=member.Academy.IsActive?await configuration.Enabled(academy,"training"):await db.AcademyFeatureEntitlements.AnyAsync(e=>e.AcademyId==academy&&e.Feature=="training"&&e.Enabled&&e.IsActive);
        if(!trainingEnabled)unavailable="خدمة التدريب غير مفعّلة في الإعدادات؛ سجلاتها محفوظة ولا تظهر في تجربة العضو.";
        else if(member.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) {
            var canRead=member.Role==AcademyRole.AcademyOwner||(member.UseActionGrants?await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==member.Id&&g.IsActive&&g.Module=="subscriptions"&&g.Read):await db.StaffGrants.AnyAsync(g=>g.MembershipId==member.Id&&g.IsActive&&g.Allowed&&g.Permission==AcademyPermissions.SubscriptionRead));
            if(canRead)subscriptions=Value(await SubscriptionHomeEndpoints.ReadOverview(t,db,time,response));
            else unavailable="هذه العضوية غير مخوّلة بقراءة الاشتراكات؛ لم نوسّع الصلاحيات للمعاينة.";
        } else if(member.Role==AcademyRole.Guardian)guardian=Value(await Slice6Endpoints.ReadHomeForMembership(t,member.UserId,db,clock,calculator,time));
        else if(member.Role==AcademyRole.Coach)nextSession=Value(await Slice4Endpoints.ReadSessionsForMembership(t,db,time,nearestOnly:true));
        if(audit is null)db.PlatformAudits.Add(new(){Id=body.RequestId,ActorUserId=operatorId,TargetAcademyId=academy,Action="academy.role-preview",Details=JsonSerializer.Serialize(new{membershipId=member.Id,role=member.Role.ToString(),readOnly=true,preparation=!member.Academy.IsActive}),AtUtc=time.GetUtcNow()});
        await db.SaveChangesAsync();await tx.CommitAsync();
        return Results.Ok(new{readOnly=true,label="معاينة دور — قراءة فقط",previewId=body.RequestId,generatedAt=time.GetUtcNow(),academyId=academy,academyName=member.Academy.ArabicName,member=member.User.DisplayName,role=member.Role.ToString(),preparation=!member.Academy.IsActive,scope=new{member.AllBranches,member.AllSports,branches=await db.Branches.Where(b=>b.IsActive).Select(b=>b.ArabicName).ToArrayAsync(),sports=await db.Sports.Where(s=>s.IsActive).Select(s=>s.ArabicName).ToArrayAsync()},subscriptions,guardian,nextSession,unavailable});
    }
}
