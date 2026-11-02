using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice6;

public static class Slice6DemoSeed
{
    public static readonly Guid PendingRequestId = Guid.Parse("71000000-0000-0000-0000-000000000001");
    public static readonly Guid ApprovedRequestId = Guid.Parse("71000000-0000-0000-0000-000000000002");
    public static readonly Guid RejectedRequestId = Guid.Parse("71000000-0000-0000-0000-000000000003");
    public static readonly Guid NewChildRequestId = Guid.Parse("71000000-0000-0000-0000-000000000004");
    public static readonly Guid AcademyBRequestId = Guid.Parse("72000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(FoundationDbContext db, Guid guardianUserId, Guid reviewerUserId, DateTimeOffset now, CancellationToken ct)
    {
        var swimmingEnrollment = await db.SportEnrollments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.SwimmingId).Select(x => x.Id).SingleAsync(ct);
        await Add(db, new NewEnrollmentRequest { Id = PendingRequestId, AcademyId = DemoSeed.NogoomAcademyId, RequestedByGuardianUserId = guardianUserId, RequestType = NewEnrollmentRequestType.ExistingChildNewSport, ExistingPlayerId = Slice2DemoSeed.OmarPlayerId, SportId = Slice2DemoSeed.BasketballId, PreferredBranchId = Slice2DemoSeed.CityBranchId, Notes = "يفضل موعدًا مسائيًا", Status = NewEnrollmentRequestStatus.Pending, IdempotencyKey = "demo-slice6-pending", CreatedAtUtc = now.AddDays(-1), UpdatedAtUtc = now.AddDays(-1) }, ct);
        await Add(db, new NewEnrollmentRequest { Id = ApprovedRequestId, AcademyId = DemoSeed.NogoomAcademyId, RequestedByGuardianUserId = guardianUserId, RequestType = NewEnrollmentRequestType.ExistingChildNewSport, ExistingPlayerId = Slice2DemoSeed.OmarPlayerId, SportId = Slice2DemoSeed.SwimmingId, PreferredBranchId = Slice2DemoSeed.FifthBranchId, Status = NewEnrollmentRequestStatus.Approved, ApprovedPlayerId = Slice2DemoSeed.OmarPlayerId, CreatedSportEnrollmentId = swimmingEnrollment, ReviewedByUserId = reviewerUserId, ReviewedAtUtc = now.AddDays(-3), IdempotencyKey = "demo-slice6-approved", CreatedAtUtc = now.AddDays(-4), UpdatedAtUtc = now.AddDays(-3) }, ct);
        await Add(db, new NewEnrollmentRequest { Id = RejectedRequestId, AcademyId = DemoSeed.NogoomAcademyId, RequestedByGuardianUserId = guardianUserId, RequestType = NewEnrollmentRequestType.ExistingChildNewSport, ExistingPlayerId = Slice2DemoSeed.MariamPlayerId, SportId = Slice2DemoSeed.SwimmingId, PreferredBranchId = Slice2DemoSeed.FifthBranchId, Status = NewEnrollmentRequestStatus.Rejected, GuardianVisibleReason = "لا توجد مجموعة مناسبة للعمر حاليًا.", ReviewedByUserId = reviewerUserId, ReviewedAtUtc = now.AddDays(-2), IdempotencyKey = "demo-slice6-rejected", CreatedAtUtc = now.AddDays(-3), UpdatedAtUtc = now.AddDays(-2) }, ct);
        await Add(db, new NewEnrollmentRequest { Id = NewChildRequestId, AcademyId = DemoSeed.NogoomAcademyId, RequestedByGuardianUserId = guardianUserId, RequestType = NewEnrollmentRequestType.NewChild, NewChildArabicName = "سليم أحمد محمود", NewChildDateOfBirth = new DateOnly(2020, 6, 10), NewChildGender = Gender.Male, SportId = Slice2DemoSeed.SwimmingId, PreferredBranchId = Slice2DemoSeed.FifthBranchId, Status = NewEnrollmentRequestStatus.Pending, IdempotencyKey = "demo-slice6-new-child", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);

        var bGuardian = await db.GuardianProfiles.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => new { x.UserId }).SingleAsync(ct);
        var bSport = await db.Sports.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync(ct);
        var bBranch = await db.Branches.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync(ct);
        await Add(db, new NewEnrollmentRequest { Id = AcademyBRequestId, AcademyId = DemoSeed.FutureAcademyId, RequestedByGuardianUserId = bGuardian.UserId, RequestType = NewEnrollmentRequestType.NewChild, NewChildArabicName = "طفل أكاديمية المستقبل", NewChildDateOfBirth = new DateOnly(2018, 1, 1), SportId = bSport, PreferredBranchId = bBranch, Status = NewEnrollmentRequestStatus.Pending, IdempotencyKey = "demo-slice6-academy-b", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
    }

    private static async Task Add(FoundationDbContext db, NewEnrollmentRequest entity, CancellationToken ct)
    {
        if (await db.NewEnrollmentRequests.AnyAsync(x => x.Id == entity.Id, ct)) return;
        db.NewEnrollmentRequests.Add(entity); await db.SaveChangesAsync(ct);
    }
}
