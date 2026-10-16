using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// One branch policy shared by settings, signed attendance and exception review.
public sealed record StaffAttendancePolicy(bool Enabled, int RadiusMeters, int MaxAccuracyMeters,
    int MaxAgeSeconds, int ChallengeSeconds, bool CoachRequired, bool AdminRequired, bool OwnerRequired,
    Guid? ReviewerMembershipId)
{
    public static StaffAttendancePolicy Read(Branch b) => new(b.StaffCheckInEnabled,b.StaffCheckInRadiusMeters,
        b.StaffCheckInMaxAccuracyMeters,b.StaffCheckInMaxAgeSeconds,b.StaffCheckInChallengeSeconds,
        b.StaffCheckInCoachRequired,b.StaffCheckInAdminRequired,b.StaffCheckInOwnerRequired,b.StaffCheckInReviewerMembershipId);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Valid => RadiusMeters is >=10 and <=500 && MaxAccuracyMeters is >=1 and <=100 && MaxAccuracyMeters<=RadiusMeters
        && MaxAgeSeconds is >=5 and <=60 && ChallengeSeconds is >=30 and <=120;
    public bool Required(AcademyRole role) => role switch { AcademyRole.Coach=>CoachRequired, AcademyRole.AcademyAdmin=>AdminRequired, AcademyRole.AcademyOwner=>OwnerRequired, _=>false };
    public void Apply(Branch b)
    {
        b.StaffCheckInEnabled=Enabled;b.StaffCheckInRadiusMeters=RadiusMeters;b.StaffCheckInMaxAccuracyMeters=MaxAccuracyMeters;
        b.StaffCheckInMaxAgeSeconds=MaxAgeSeconds;b.StaffCheckInChallengeSeconds=ChallengeSeconds;
        b.StaffCheckInCoachRequired=CoachRequired;b.StaffCheckInAdminRequired=AdminRequired;b.StaffCheckInOwnerRequired=OwnerRequired;
        b.StaffCheckInReviewerMembershipId=ReviewerMembershipId;
    }
    public static async Task<bool> CanReview(FoundationDbContext db,TenantMembership t,Guid branch)
    {
        // The owner keeps an explicit fallback. This does not bypass the existing no-self-review guard.
        if(t.Role==AcademyRole.AcademyOwner)return true;
        var reviewer=await db.Branches.Where(b=>b.Id==branch&&b.AcademyId==t.AcademyId).Select(b=>b.StaffCheckInReviewerMembershipId).SingleOrDefaultAsync();
        return reviewer is null||reviewer==t.MembershipId;
    }
    public static async Task<bool> EligibleReviewer(FoundationDbContext db,Guid academy,Guid branch,Guid member)
    {
        var m=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.AcademyId==academy&&m.Id==member&&m.IsActive&&m.User.IsActive);
        if(m is null||!await StaffAttendanceExceptions.ManagementAllowed(db,new(academy,"",m.Role,m.Id),true))return false;
        return m.Role==AcademyRole.AcademyOwner||m.AllBranches||await db.MembershipBranchAccesses.AnyAsync(a=>a.AcademyId==academy&&a.MembershipId==member&&a.BranchId==branch&&a.IsActive);
    }
}
