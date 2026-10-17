using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static class CommunityAds
{
    internal static Guid Actor(ClaimsPrincipal p)=>Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    internal static Task<bool> FullOwner(FoundationDbContext db,TenantMembership t)=>t.Role!=AcademyRole.AcademyOwner||t.AdditionalContext?Task.FromResult(false):db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.AcademyId==t.AcademyId&&m.Role==AcademyRole.AcademyOwner&&m.IsActive&&m.User.IsActive&&m.AllBranches&&m.AllSports);
    internal static async Task<bool> Enabled(AcademyConfigurationService config,Guid a)=>await config.Enabled(a,"community_ads")&&(await config.Active(a))?.TenantMarketingOptIn==true;
    internal static IQueryable<Guid> EligibleBranches(FoundationDbContext db,Guid academy,Guid user,DateOnly today)=>
        db.SportEnrollments.Where(e=>e.AcademyId==academy&&e.IsActive&&e.Status==EnrollmentStatus.Active&&e.Player.IsActive&&e.Branch.IsActive&&e.Sport.IsActive
            &&db.GuardianPlayerLinks.Any(l=>l.AcademyId==academy&&l.PlayerId==e.PlayerId&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.UserId==user)
            &&db.SubscriptionPeriods.Any(p=>p.AcademyId==academy&&p.SportEnrollmentId==e.Id&&p.IsActive&&(p.Status==SubscriptionPeriodStatus.Active||p.Status==SubscriptionPeriodStatus.Scheduled)&&p.StartDate<=today&&(p.EndDate.HasValue?p.EndDate>=today:p.RemainingSessions>0)))
            .Select(e=>e.BranchId).Distinct();
    internal static IQueryable<CommunityCampaignRevision> Live(FoundationDbContext db,Guid a,DateTimeOffset now)=>db.CommunityCampaignRevisions.Where(r=>r.AcademyId==a&&r.State=="Approved"&&r.StartsAtUtc<=now&&r.EndsAtUtc>now&&db.CommunityCampaigns.Any(c=>c.Id==r.CampaignId&&c.IsActive&&c.CurrentRevision==r.Revision)&&(!r.ImageAssetId.HasValue||db.PrivateAssets.Any(a=>a.Id==r.ImageAssetId&&a.IsActive&&a.Purpose=="Campaign")));
    internal static async Task<CommunityCampaignTarget?> AuthorizedTarget(FoundationDbContext db,AcademyConfigurationService config,TenantMembership t,Guid user,Guid revision,TimeProvider clock,DateOnly subscriptionAsOf)
    {
        if(t.Role!=AcademyRole.Guardian||!await Enabled(config,t.AcademyId)||!await db.GuardianSponsorPreferences.AnyAsync(p=>p.AcademyId==t.AcademyId&&p.UserId==user&&p.IsActive&&p.InAppOptIn&&p.AdultDeclaredAtUtc!=null&&p.ConsentVersion==1))return null;
        var branches=EligibleBranches(db,t.AcademyId,user,subscriptionAsOf);var live=Live(db,t.AcademyId,clock.GetUtcNow());
        return await db.CommunityCampaignTargets.AsNoTracking().Where(x=>x.AcademyId==t.AcademyId&&x.RevisionId==revision&&x.Decision=="Approved"&&branches.Contains(x.BranchId)&&live.Any(r=>r.Id==x.RevisionId)).OrderBy(x=>x.BranchId).FirstOrDefaultAsync();
    }
    internal static async Task<object> Card(FoundationDbContext db,CommunityCampaignRevision r,Guid branch,DateOnly subscriptionAsOf)=>new{r.Id,r.CampaignId,r.Revision,r.Advertiser,r.Title,r.Body,r.ButtonText,r.ImageAlt,imageUrl=r.ImageAssetId.HasValue?$"/api/v1/assets/{r.ImageAssetId}":null,branchId=branch,branchName=await db.Branches.Where(b=>b.Id==branch).Select(b=>b.ArabicName).SingleAsync(),subscriptionAsOf,r.StartsAtUtc,r.EndsAtUtc,label="محتوى برعاية",placement="GuardianHome",destination="InternalDetails",detailUrl=$"/api/v1/guardian/community-ads/{r.Id}"};
    internal static string State(CommunityCampaignRevision r,DateTimeOffset now)=>r.State=="Approved"?(now<r.StartsAtUtc?"Scheduled":now>=r.EndsAtUtc?"Ended":"Active"):r.State;
}
