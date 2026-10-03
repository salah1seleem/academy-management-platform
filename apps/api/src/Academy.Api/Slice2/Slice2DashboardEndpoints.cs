using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice2;

public static class Slice2DashboardEndpoints
{
    public static void MapSlice2DashboardEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var manage = api.MapGroup("/manage").RequireAuthorization(AcademyPermissions.StructureManage);

        manage.MapGet("/structure/{kind}", ListStructure);
        manage.MapGet("/structure/{kind}/{id:guid}", GetStructure);
        manage.MapPut("/structure/{kind}/{id:guid}", UpdateStructure).AddEndpointFilter<CsrfFilter>();
        manage.MapPut("/structure/{kind}/{id:guid}/status", SetStructureStatus).AddEndpointFilter<CsrfFilter>();
        manage.MapPut("/group-assignments/{assignmentId:guid}/status", SetAssignmentStatus).AddEndpointFilter<CsrfFilter>();

        var people = api.MapGroup("/people").RequireAuthorization(AcademyPermissions.PeopleManage);
        people.MapGet("/players/{playerId:guid}", GetPlayer);
        people.MapPut("/players/{playerId:guid}", UpdatePlayer).AddEndpointFilter<CsrfFilter>();
        people.MapPut("/players/{playerId:guid}/status", SetPlayerStatus).AddEndpointFilter<CsrfFilter>();
        people.MapGet("/guardians/{guardianId:guid}", GetGuardian);
        people.MapPut("/guardians/{guardianId:guid}", UpdateGuardian).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<IResult> ListStructure(string kind, CurrentTenant tenant, FoundationDbContext db)
    {
        var current = (await tenant.ResolveAsync())!;
        object? result = kind switch
        {
            "branches" => await db.Branches.AsNoTracking().Where(x => x.AcademyId == current.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.EnglishName, x.IsActive }).ToListAsync(),
            "sports" => await db.Sports.AsNoTracking().Where(x => x.AcademyId == current.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.EnglishName, x.IsActive }).ToListAsync(),
            "categories" => await db.AgeCategories.AsNoTracking().Where(x => x.AcademyId == current.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.MinimumBirthYear, x.MaximumBirthYear, x.IsActive }).ToListAsync(),
            "groups" => await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == current.AcademyId).OrderBy(x => x.ArabicName).Select(x => new
            {
                x.Id, x.ArabicName, x.IsActive, branchName = x.Branch.ArabicName, sportName = x.Sport.ArabicName, categoryName = x.AgeCategory.ArabicName,
                playerCount = db.SportEnrollments.Count(e => e.AcademyId == current.AcademyId && e.TrainingGroupId == x.Id && e.IsActive),
                coaches = db.StaffGroupAssignments.Where(a => a.AcademyId == current.AcademyId && a.TrainingGroupId == x.Id && a.IsActive && a.AcademyMembership.IsActive).OrderBy(a => a.AcademyMembership.User.DisplayName).Select(a => a.AcademyMembership.User.DisplayName).ToList(),
                schedules = db.RecurringSchedules.Where(s => s.AcademyId == current.AcademyId && s.TrainingGroupId == x.Id && s.IsActive).OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).Select(s => new { s.DayOfWeek, s.StartTime, s.EndTime }).ToList()
            }).ToListAsync(),
            "coaches" => await db.AcademyMemberships.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Role == Infrastructure.Tenancy.AcademyRole.Coach).OrderBy(x => x.User.DisplayName).Select(x => new
            {
                id = x.Id, arabicName = x.User.DisplayName, x.IsActive,
                assignedGroups = db.StaffGroupAssignments.Count(a => a.AcademyId == current.AcademyId && a.AcademyMembershipId == x.Id && a.IsActive),
                groups = db.StaffGroupAssignments.Where(a => a.AcademyId == current.AcademyId && a.AcademyMembershipId == x.Id && a.IsActive).OrderBy(a => a.TrainingGroup.ArabicName).Select(a => new { id = a.TrainingGroupId, name = a.TrainingGroup.ArabicName, branch = a.TrainingGroup.Branch.ArabicName }).ToList()
            }).ToListAsync(),
            _ => null
        };
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetStructure(string kind, Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var current = (await tenant.ResolveAsync())!;
        object? result = kind switch
        {
            "branches" => await db.Branches.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id).Select(x => new { x.Id, x.ArabicName, x.EnglishName, description = x.Address, x.IsActive }).SingleOrDefaultAsync(),
            "sports" => await db.Sports.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id).Select(x => new { x.Id, x.ArabicName, x.EnglishName, x.IsActive }).SingleOrDefaultAsync(),
            "categories" => await db.AgeCategories.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id).Select(x => new { x.Id, x.ArabicName, x.MinimumBirthYear, x.MaximumBirthYear, x.IsActive }).SingleOrDefaultAsync(),
            "groups" => await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id).Select(x => new { x.Id, x.ArabicName, x.EnglishName, x.BranchId, x.SportId, x.AgeCategoryId, x.Capacity, x.IsActive, branchName = x.Branch.ArabicName, sportName = x.Sport.ArabicName, categoryName = x.AgeCategory.ArabicName }).SingleOrDefaultAsync(),
            "coaches" => await db.AcademyMemberships.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id && x.Role == Infrastructure.Tenancy.AcademyRole.Coach).Select(x => new { id = x.Id, arabicName = x.User.DisplayName, x.IsActive, assignments = x.Id == Guid.Empty ? Array.Empty<object>() : db.StaffGroupAssignments.Where(a => a.AcademyId == current.AcademyId && a.AcademyMembershipId == x.Id).OrderBy(a => a.TrainingGroup.ArabicName).Select(a => new { a.Id, a.TrainingGroupId, groupName = a.TrainingGroup.ArabicName, a.IsActive }).ToArray() }).SingleOrDefaultAsync(),
            _ => null
        };
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> UpdateStructure(string kind, Guid id, StructureUpdateRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var current = (await tenant.ResolveAsync())!;
        if (string.IsNullOrWhiteSpace(request.ArabicName)) return Validation("arabicName", "الاسم بالعربية مطلوب.");
        var now = clock.GetUtcNow();
        switch (kind)
        {
            case "branches":
                var branch = await db.Branches.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id);
                if (branch is null) return Results.NotFound();
                branch.ArabicName = request.ArabicName.Trim(); branch.EnglishName = Clean(request.EnglishName); branch.Address = Clean(request.Description); branch.UpdatedAtUtc = now;
                break;
            case "sports":
                var sport = await db.Sports.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id);
                if (sport is null) return Results.NotFound();
                sport.ArabicName = request.ArabicName.Trim(); sport.EnglishName = Clean(request.EnglishName); sport.UpdatedAtUtc = now;
                break;
            case "categories":
                var category = await db.AgeCategories.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id);
                if (category is null) return Results.NotFound();
                category.ArabicName = request.ArabicName.Trim(); category.MinimumBirthYear = request.MinimumBirthYear; category.MaximumBirthYear = request.MaximumBirthYear; category.UpdatedAtUtc = now;
                break;
            case "groups":
                var group = await db.TrainingGroups.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id);
                if (group is null) return Results.NotFound();
                if (request.BranchId is null || request.SportId is null || request.AgeCategoryId is null ||
                    !await db.Branches.AnyAsync(x => x.AcademyId == current.AcademyId && x.Id == request.BranchId && x.IsActive) ||
                    !await db.Sports.AnyAsync(x => x.AcademyId == current.AcademyId && x.Id == request.SportId && x.IsActive) ||
                    !await db.AgeCategories.AnyAsync(x => x.AcademyId == current.AcademyId && x.Id == request.AgeCategoryId && x.IsActive))
                    return Results.UnprocessableEntity(new { message = "اختيارات المجموعة غير صالحة." });
                var hasEnrollments = await db.SportEnrollments.AnyAsync(x => x.AcademyId == current.AcademyId && x.TrainingGroupId == id);
                if (hasEnrollments && (group.BranchId != request.BranchId || group.SportId != request.SportId))
                    return Results.Conflict(new { message = "لا يمكن تغيير فرع أو رياضة مجموعة مرتبطة بتسجيلات قائمة." });
                group.ArabicName = request.ArabicName.Trim(); group.EnglishName = Clean(request.EnglishName); group.BranchId = request.BranchId.Value; group.SportId = request.SportId.Value; group.AgeCategoryId = request.AgeCategoryId.Value; group.Capacity = request.Capacity; group.UpdatedAtUtc = now;
                break;
            default: return Results.NotFound();
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> SetStructureStatus(string kind, Guid id, StatusRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var current = (await tenant.ResolveAsync())!;
        TenantEntity? entity = kind switch
        {
            "branches" => await db.Branches.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id),
            "sports" => await db.Sports.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id),
            "categories" => await db.AgeCategories.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id),
            "groups" => await db.TrainingGroups.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == id),
            _ => null
        };
        if (entity is null) return Results.NotFound();
        entity.IsActive = request.IsActive; entity.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> SetAssignmentStatus(Guid assignmentId, StatusRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var assignment = await db.StaffGroupAssignments.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == assignmentId);
        if (assignment is null) return Results.NotFound();
        assignment.IsActive = request.IsActive; assignment.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> GetPlayer(Guid playerId, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var player = await db.Players.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == playerId).Select(x => new
        {
            x.Id, x.PlayerCode, x.ArabicName, x.EnglishName, x.DateOfBirth, x.Gender, x.HeightCm, x.WeightKg, x.PreferredFoot, x.FootballPosition, x.Address, x.IsActive, x.PhotoReference,
            asOfDate = clock.Today,
            guardians = db.GuardianPlayerLinks.Where(l => l.AcademyId == current.AcademyId && l.PlayerId == x.Id && l.IsActive && l.Guardian.IsActive)
                .OrderBy(l => l.Guardian.DisplayName).Select(l => new { id = l.GuardianId, l.Guardian.DisplayName, l.RelationshipType }).ToList(),
            enrollments = db.SportEnrollments.Where(e => e.AcademyId == current.AcademyId && e.PlayerId == x.Id).OrderBy(e => e.Sport.ArabicName).Select(e => new
            {
                e.Id,
                sportName = e.Sport.ArabicName,
                branchName = e.Branch.ArabicName,
                groupName = e.TrainingGroup.ArabicName,
                categoryName = e.TrainingGroup.AgeCategory.ArabicName,
                status = e.Status.ToString(),
                subscription = db.SubscriptionPeriods.Where(period => period.AcademyId == current.AcademyId && period.SportEnrollmentId == e.Id)
                    .OrderByDescending(period => period.UpdatedAtUtc)
                    .Select(period => new
                    {
                        period.Id,
                        plan = period.SubscriptionPlan.ArabicName,
                        period.StartDate,
                        period.EndDate,
                        status = period.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : period.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : period.EndDate < clock.Today || (period.SubscriptionPlan.PlanType != SubscriptionPlanType.Duration && period.RemainingSessions <= 0) ? "Expired" : period.StartDate > clock.Today ? "Scheduled" : "Active"
                    }).FirstOrDefault()
            }).ToList()
        }).SingleOrDefaultAsync();
        return player is null ? Results.NotFound() : Results.Ok(player);
    }

    private static async Task<IResult> UpdatePlayer(Guid playerId, PlayerUpdateRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var current = (await tenant.ResolveAsync())!;
        if (string.IsNullOrWhiteSpace(request.ArabicName)) return Validation("arabicName", "اسم اللاعب مطلوب.");
        var player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == playerId);
        if (player is null) return Results.NotFound();
        player.ArabicName = request.ArabicName.Trim(); player.EnglishName = Clean(request.EnglishName); player.DateOfBirth = request.DateOfBirth; player.Gender = request.Gender; player.HeightCm = request.HeightCm; player.WeightKg = request.WeightKg; player.PreferredFoot = request.PreferredFoot; player.FootballPosition = Clean(request.FootballPosition); player.Address = Clean(request.Address); player.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> SetPlayerStatus(Guid playerId, StatusRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == playerId);
        if (player is null) return Results.NotFound();
        player.IsActive = request.IsActive; player.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> GetGuardian(Guid guardianId, CurrentTenant tenant, FoundationDbContext db)
    {
        var current = (await tenant.ResolveAsync())!;
        var guardian = await db.GuardianProfiles.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == guardianId).Select(x => new
        {
            x.Id, x.DisplayName, x.ContactPhone, x.IsActive,
            children = db.GuardianPlayerLinks.Where(l => l.AcademyId == current.AcademyId && l.GuardianId == x.Id && l.IsActive).OrderBy(l => l.Player.ArabicName).Select(l => new { l.Player.Id, l.Player.PlayerCode, l.Player.ArabicName, l.RelationshipType, l.Player.IsActive }).ToList()
        }).SingleOrDefaultAsync();
        return guardian is null ? Results.NotFound() : Results.Ok(guardian);
    }

    private static async Task<IResult> UpdateGuardian(Guid guardianId, GuardianUpdateRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var phone = EgyptPhoneNormalizer.Normalize(request.ContactPhone);
        if (string.IsNullOrWhiteSpace(request.DisplayName) || phone is null) return Validation("guardian", "اسم ولي الأمر ورقم هاتف صحيح مطلوبان.");
        var guardian = await db.GuardianProfiles.SingleOrDefaultAsync(x => x.AcademyId == current.AcademyId && x.Id == guardianId);
        if (guardian is null) return Results.NotFound();
        var phoneInUse = await db.GuardianProfiles.AnyAsync(x => x.AcademyId == current.AcademyId && x.Id != guardianId && x.ContactPhone == phone);
        if (phoneInUse) return Results.Conflict(new { message = "رقم التواصل مستخدم لولي أمر آخر داخل الأكاديمية." });
        guardian.DisplayName = request.DisplayName.Trim(); guardian.ContactPhone = phone; guardian.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record StructureUpdateRequest(string ArabicName, string? EnglishName, string? Description, int? MinimumBirthYear, int? MaximumBirthYear, Guid? BranchId, Guid? SportId, Guid? AgeCategoryId, int? Capacity);
public sealed record StatusRequest(bool IsActive);
public sealed record PlayerUpdateRequest(string ArabicName, string? EnglishName, DateOnly DateOfBirth, Gender? Gender, decimal? HeightCm, decimal? WeightKg, PreferredFoot? PreferredFoot, string? FootballPosition, string? Address);
public sealed record GuardianUpdateRequest(string DisplayName, string ContactPhone);
