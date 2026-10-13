using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;

// Explicit opt-in synthetic data. Never runs for a customer or production profile.
public static class DayPassDemoSeed
{
    public static readonly Guid PlanId = Guid.Parse("58000000-0000-0000-0000-000000000080");
    public static Guid BookingId(DateOnly day) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"academity-demo-day-pass:{day:yyyy-MM-dd}"))[..16]);
    public static async Task Seed(FoundationDbContext db, TimeProvider clock)
    {
        var academy = DemoSeed.NogoomAcademyId;
        if (!await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == "nogoom-demo" && a.IsActive)
            || !await db.Branches.AnyAsync(b => b.Id == Slice2DemoSeed.FifthBranchId && b.IsActive)
            || !await db.Sports.AnyAsync(s => s.Id == Slice2DemoSeed.SwimmingId && s.IsActive)) return;
        var now = clock.GetUtcNow();
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime).AddDays(1);
        var bookingId = BookingId(day);
        var resourceId = Guid.Parse("58000000-0000-0000-0000-000000000010");
        await using var tx = await db.Database.BeginTransactionAsync();
        await PlanCatalog.LockAsync(db, academy, PlanId);
        if (!await db.SubscriptionPlans.AnyAsync(p => p.Id == PlanId))
        {
            var plan = new SubscriptionPlan { Id = PlanId, AcademyId = academy, SportId = Slice2DemoSeed.SwimmingId, ArabicName = "زيارة مسبح يومية · بيانات اصطناعية", ServiceKind = AcademyServiceKind.DayPass, PlanType = SubscriptionPlanType.Duration, DurationDays = 1, Price = 125, Currency = "EGP", AllBranches = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.SubscriptionPlans.Add(plan);
            db.PlanBranchPublications.Add(new() { Id = Guid.Parse("58000000-0000-0000-0000-000000000081"), AcademyId = academy, SubscriptionPlanId = PlanId, BranchId = Slice2DemoSeed.FifthBranchId, CreatedAtUtc = now, UpdatedAtUtc = now });
            var version = PlanPriceVersion.Create(plan, [Slice2DemoSeed.FifthBranchId], null, now); version.Id = Guid.Parse("58000000-0000-0000-0000-000000000082"); db.PlanPriceVersions.Add(version);
        }
        if (!await db.ResourceBookings.AnyAsync(b => b.Id == bookingId))
        {
            var resource = await db.FacilityResources.SingleOrDefaultAsync(r => r.Id == resourceId && r.IsActive && r.ApprovedAtUtc != null);
            if (resource is not null && resource.ServiceKinds.Contains("DayPass") && await db.Branches.AnyAsync(b => b.Id == resource.BranchId && b.IsActive)
                && !await db.ResourceBookings.AnyAsync(b => b.ResourceId == resourceId && b.Date == day && b.State == ResourceBookingState.Reserved && b.StartTime < new TimeOnly(18, 0) && b.EndTime > new TimeOnly(17, 0)))
            {
                var owner = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
                db.ResourceBookings.Add(new() { Id = bookingId, AcademyId = academy, ResourceId = resourceId, BranchId = resource.BranchId, Date = day, StartTime = new(17, 0), EndTime = new(18, 0), ServiceKind = "DayPass", ReservedSeats = 2, ApprovedCapacitySnapshot = resource.Capacity, Exclusive = true, State = ResourceBookingState.Reserved, ActorUserId = owner, RequestKey = $"demo-day-pass-{day:yyyy-MM-dd}", RequestHash = "synthetic-day-pass", CreatedAtUtc = now, UpdatedAtUtc = now });
            }
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
