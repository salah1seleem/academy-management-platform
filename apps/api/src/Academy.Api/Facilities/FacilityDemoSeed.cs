using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice2;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
public static class FacilityDemoSeed
{
    public static readonly Guid FacilityId = Guid.Parse("58000000-0000-0000-0000-000000000001");
    public static readonly Guid SpaceId = Guid.Parse("58000000-0000-0000-0000-000000000002");
    public static async Task Seed(FoundationDbContext db)
    {
        // Called ONLY for explicit Demo FacilitiesSeedEnabled + LegacyRegression.
        // Once created, respect subsequent owner edits and feature revocation.
        if (await db.Facilities.AnyAsync(x => x.Id == FacilityId)) return;
        var academy = await db.Academies.SingleAsync(x => x.Id == DemoSeed.NogoomAcademyId && x.Slug == "nogoom-demo");
        var owner = await db.Users.SingleAsync(x => x.Email == DemoSeed.OwnerEmail); var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy.Id}, 0))");
        var active = await db.AcademyConfigurationVersions.SingleAsync(x => x.AcademyId == academy.Id && x.State == ConfigurationState.Active);
        var settings = AcademyRuntimeSettings.Read(active.Payload); settings.Features["facilities"] = true; var payload = settings.Serialize();
        active.State = ConfigurationState.Superseded;
        db.AcademyConfigurationVersions.Add(new() { Id = Guid.NewGuid(), AcademyId = academy.Id, Revision = await db.AcademyConfigurationVersions.Where(x => x.AcademyId == academy.Id).MaxAsync(x => x.Revision) + 1, State = ConfigurationState.Active, Payload = payload, ContentHash = AcademyConfigurationService.Hash(payload), EffectiveAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = owner.Id });
        (await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == academy.Id && x.Feature == "facilities")).Enabled = true;
        // Flush supersession first because one active config per tenant is enforced.
        var addedConfig = db.ChangeTracker.Entries<AcademyConfigurationVersion>().Single(x => x.State == EntityState.Added); addedConfig.State = EntityState.Detached; await db.SaveChangesAsync(); db.Add(addedConfig.Entity);
        db.Facilities.Add(new() { Id = FacilityId, AcademyId = academy.Id, BranchId = Slice2DemoSeed.FifthBranchId, Name = "مركز سباحة تجريبي", CreatedAtUtc = now, UpdatedAtUtc = now });
        db.FacilitySpaces.Add(new() { Id = SpaceId, AcademyId = academy.Id, FacilityId = FacilityId, BranchId = Slice2DemoSeed.FifthBranchId, SportId = Slice2DemoSeed.SwimmingId, Name = "المسبح التعليمي", Kind = FacilitySpaceKind.Pool, CreatedAtUtc = now, UpdatedAtUtc = now });
        for (var index = 0; index < 4; index++) db.FacilityResources.Add(new() { Id = Guid.Parse($"58000000-0000-0000-0000-{index + 10:D12}"), AcademyId = academy.Id, FacilitySpaceId = SpaceId, BranchId = Slice2DemoSeed.FifthBranchId, Name = ((char)('A' + index)).ToString(), Capacity = 8, SharedBookingsAllowed = true, OperatingDays = [0, 1, 2, 3, 4, 5, 6], OpensAt = new(8, 0), ClosesAt = new(22, 0), ServiceKinds = ["Training", "OpenSwim", "DayPass"], ApprovedByUserId = index == 3 ? null : owner.Id, ApprovedAtUtc = index == 3 ? null : now, ApprovalEvidence = index == 3 ? null : "اعتماد اصطناعي للعرض فقط؛ ليس تقييم سلامة لمسبح حقيقي.", CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
