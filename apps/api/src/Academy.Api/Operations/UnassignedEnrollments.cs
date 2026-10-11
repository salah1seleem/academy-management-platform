using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Registration in a sport is not group placement, a payment, or a training entitlement.
public static class UnassignedEnrollments
{
    public static void MapUnassignedEnrollments(this WebApplication app)
    {
        var people = app.MapGroup("/api/v1/people").RequireAuthorization(AcademyPermissions.PeopleManage)
            .WithMetadata(new AcademyFeatureMetadata("training"));
        people.MapGet("/unassigned/options", async (FoundationDbContext db) => Results.Ok(new
        {
            branches = await db.Branches.Where(x => x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName }).ToListAsync(),
            sports = await db.Sports.Where(x => x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName }).ToListAsync()
        }));
        people.MapPost("/players/{id:guid}/unassigned-enrollments", Create).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<IResult> Create(Guid id, UnassignedEnrollmentWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!Enum.IsDefined(body.RegistrationFormat)) return Results.UnprocessableEntity(new { message = "نوع التسجيل غير صالح." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + t.AcademyId}, 0))");
        var member = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == t.MembershipId && x.IsActive && x.User.IsActive && x.Academy.IsActive);
        var payload = await db.AcademyConfigurationVersions.AsNoTracking().Where(x => x.State == ConfigurationState.Active).Select(x => x.Payload).SingleOrDefaultAsync();
        if (member is null || member.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) || payload is null || !AcademyRuntimeSettings.Read(payload).Features.GetValueOrDefault("training") || !await db.AcademyFeatureEntitlements.AnyAsync(x => x.Feature == "training" && x.Enabled && x.IsActive)) return Results.Forbid();
        if (member.Role == AcademyRole.AcademyAdmin)
        {
            var grant = await db.StaffActionGrants.AsNoTracking().SingleOrDefaultAsync(x => x.MembershipId == member.Id && x.Module == "players" && x.IsActive);
            if (member.UseActionGrants ? grant?.Read != true || !grant.Add : await db.StaffGrants.AnyAsync(x => x.MembershipId == member.Id && x.Permission == AcademyPermissions.PeopleManage && x.IsActive && !x.Allowed)) return Results.Forbid();
            if (!member.AllBranches && !await db.MembershipBranchAccesses.AnyAsync(x => x.MembershipId == member.Id && x.BranchId == body.BranchId && x.IsActive)) return Results.NotFound();
        }
        if (!await db.Players.AnyAsync(x => x.Id == id && x.IsActive) || !await db.Branches.AnyAsync(x => x.Id == body.BranchId && x.IsActive) || !await db.Sports.AnyAsync(x => x.Id == body.SportId && x.IsActive)) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"intake:{t.AcademyId}:{id}:{body.SportId}:{body.BranchId}"}, 0))");
        var existing = await db.SportEnrollments.AsNoTracking().Where(x => x.PlayerId == id && x.SportId == body.SportId && x.BranchId == body.BranchId && x.RegistrationFormat == body.RegistrationFormat && x.IsActive).OrderBy(x => x.Id).ToListAsync();
        if (existing.Any(x => x.TrainingGroupId.HasValue)) return Results.Conflict(new { message = "للاعب تسجيل مسكن بالفعل في هذه الرياضة والفرع. استخدم نقل المجموعة بدل تكرار التسجيل." });
        if (existing.Count != 0) return Results.Ok(new { id = existing[0].Id, playerId = id, placement = "Unassigned", subscriptionCreated = false, replayed = true });
        var row = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = id, SportId = body.SportId, BranchId = body.BranchId, RegistrationFormat = body.RegistrationFormat, TrainingGroupId = null, Status = EnrollmentStatus.Active, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.SportEnrollments.Add(row);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/api/v1/people/players/{id}", new { row.Id, playerId = id, placement = "Unassigned", subscriptionCreated = false, replayed = false });
    }
}
public sealed record UnassignedEnrollmentWrite(Guid SportId, Guid BranchId, TrainingRegistrationFormat RegistrationFormat = TrainingRegistrationFormat.Group);
