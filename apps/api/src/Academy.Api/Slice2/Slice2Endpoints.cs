using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice2;

public static class Slice2Endpoints
{
    public static void MapSlice2Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var manage = api.MapGroup("/manage").RequireAuthorization(AcademyPermissions.StructureManage);

        manage.MapGet("/structure/options", async (CurrentTenant tenant, FoundationDbContext db) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var branches = await db.Branches.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName }).ToListAsync();
            var sports = await db.Sports.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName }).ToListAsync();
            var categories = await db.AgeCategories.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName }).ToListAsync();
            var groups = await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName)
                .Select(x => new { x.Id, x.ArabicName, x.BranchId, x.SportId, x.AgeCategoryId }).ToListAsync();
            var coaches = await db.AcademyMemberships.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Role == AcademyRole.Coach && x.IsActive).OrderBy(x => x.User.DisplayName).Select(x => new { id = x.Id, arabicName = x.User.DisplayName }).ToListAsync();
            return Results.Ok(new { branches, sports, categories, groups, coaches });
        });

        manage.MapPost("/branches", async (NamedRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!; var entity = new Branch { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = request.ArabicName.Trim(), EnglishName = request.EnglishName, Address = request.Description, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.Branches.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/v1/manage/branches/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/sports", async (NamedRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!; var entity = new Sport { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = request.ArabicName.Trim(), EnglishName = request.EnglishName, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.Sports.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/v1/manage/sports/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/age-categories", async (AgeCategoryRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!; var entity = new AgeCategory { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = request.ArabicName.Trim(), MinimumBirthYear = request.MinimumBirthYear, MaximumBirthYear = request.MaximumBirthYear, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.AgeCategories.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/v1/manage/age-categories/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/groups", async (GroupRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!;
            if (!await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.BranchId && x.IsActive) || !await db.Sports.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SportId && x.IsActive) || !await db.AgeCategories.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.AgeCategoryId && x.IsActive)) return Results.UnprocessableEntity(new { message = "اختيارات المجموعة غير صالحة." });
            var entity = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = request.ArabicName.Trim(), BranchId = request.BranchId, SportId = request.SportId, AgeCategoryId = request.AgeCategoryId, Capacity = request.Capacity, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.TrainingGroups.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/v1/manage/groups/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/groups/{groupId:guid}/schedules", async (Guid groupId, ScheduleRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!; if (request.EndTime <= request.StartTime || !await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId)) return Results.UnprocessableEntity();
            var entity = new RecurringSchedule { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = groupId, DayOfWeek = request.DayOfWeek, StartTime = request.StartTime, EndTime = request.EndTime, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.RecurringSchedules.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/v1/manage/schedules/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/groups/{groupId:guid}/coaches", async (Guid groupId, CoachAssignmentRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var groupExists = await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId && x.IsActive);
            var coach = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.MembershipId && x.Role == AcademyRole.Coach && x.IsActive);
            if (!groupExists || coach is null) return Results.UnprocessableEntity(new { message = "المدرب أو المجموعة خارج نطاق الأكاديمية." });
            var entity = new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, AcademyMembershipId = coach.Id, TrainingGroupId = groupId, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.StaffGroupAssignments.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/v1/manage/group-assignments/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();

        var people = api.MapGroup("/people").RequireAuthorization(AcademyPermissions.PeopleManage);
        people.MapGet("/players", SearchPlayers);
        people.MapGet("/guardians", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.GuardianProfiles.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.DisplayName).Select(x => new { x.Id, x.DisplayName, x.ContactPhone }).ToListAsync()); });
        people.MapPost("/registrations", RegisterPlayer).AddEndpointFilter<CsrfFilter>();

        api.MapGet("/guardian/children", GuardianChildren).RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        api.MapGet("/guardian/children/{playerId:guid}", async (Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db) =>
        {
            var t = (await tenant.ResolveAsync())!; var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var child = await db.GuardianPlayerLinks.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == userId && x.PlayerId == playerId && x.IsActive && x.Player.IsActive).Select(x => new { x.Player.Id, x.Player.PlayerCode, x.Player.ArabicName }).SingleOrDefaultAsync();
            return child is null ? Results.NotFound() : Results.Ok(child);
        }).RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        api.MapGet("/coach/groups", async (CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db) =>
        {
            var t = (await tenant.ResolveAsync())!; var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var groups = await db.StaffGroupAssignments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.AcademyMembership.UserId == userId && x.IsActive).Select(x => new { x.TrainingGroup.Id, x.TrainingGroup.ArabicName }).ToListAsync(); return Results.Ok(groups);
        }).RequireAuthorization(AcademyPermissions.CoachGroupsRead);
    }

    private static async Task<IResult> SearchPlayers(string? search, Guid? branchId, Guid? sportId, Guid? ageCategoryId, Guid? groupId, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var query = db.Players.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.ArabicName.Contains(search) || x.PlayerCode.Contains(search) || db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.Id && l.Guardian.ContactPhone == EgyptPhoneNormalizer.Normalize(search)));
        if (branchId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.BranchId == branchId));
        if (sportId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.SportId == sportId));
        if (ageCategoryId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.TrainingGroup.AgeCategoryId == ageCategoryId));
        if (groupId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.TrainingGroupId == groupId));
        return Results.Ok(await query.OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.PlayerCode, x.ArabicName, x.DateOfBirth, enrollments = db.SportEnrollments.Count(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.Status == EnrollmentStatus.Active) }).ToListAsync());
    }

    private static async Task<IResult> GuardianChildren(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var children = await db.GuardianPlayerLinks.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == userId && x.IsActive && x.Player.IsActive).OrderBy(x => x.Player.ArabicName).Select(x => new { x.Player.Id, x.Player.PlayerCode, x.Player.ArabicName }).ToListAsync();
        return Results.Ok(children);
    }

    private static async Task<IResult> RegisterPlayer(RegistrationRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, UserManager<ApplicationUser> users, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var group = await db.TrainingGroups.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.GroupId && x.IsActive);
        if (group is null || group.BranchId != request.BranchId || group.SportId != request.SportId) return Results.UnprocessableEntity(new { message = "المجموعة لا تطابق الفرع والرياضة المختارين." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        Player player;
        if (request.ExistingPlayerId is Guid playerId)
        {
            player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId && x.IsActive) ?? null!;
            if (player is null) return Results.NotFound();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.PlayerArabicName) || request.DateOfBirth is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["player"] = ["اسم اللاعب وتاريخ الميلاد مطلوبان."] });
            player = new Player { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerCode = $"PLR-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), ArabicName = request.PlayerArabicName.Trim(), DateOfBirth = request.DateOfBirth.Value, Gender = request.Gender, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.Players.Add(player);
        }
        if (await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == player.Id && x.SportId == request.SportId && x.Status == EnrollmentStatus.Active)) return Results.Conflict(new { message = "للاعب تسجيل نشط في هذه الرياضة بالفعل." });
        GuardianProfile guardian;
        if (request.ExistingGuardianId is Guid guardianId)
            guardian = await db.GuardianProfiles.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == guardianId && x.IsActive) ?? null!;
        else
        {
            var phone = EgyptPhoneNormalizer.Normalize(request.GuardianPhone);
            if (phone is null || string.IsNullOrWhiteSpace(request.GuardianName)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["guardian"] = ["اسم ولي الأمر ورقم هاتف صحيح مطلوبان."] });
            var user = await users.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone);
            if (user is null) { user = new ApplicationUser { Id = Guid.NewGuid(), UserName = phone, PhoneNumber = phone, PhoneNumberConfirmed = false, DisplayName = request.GuardianName.Trim(), IsActive = true, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }; var result = await users.CreateAsync(user); if (!result.Succeeded) return Results.ValidationProblem(new Dictionary<string, string[]> { ["guardian"] = result.Errors.Select(x => x.Description).ToArray() }); }
            var membership = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.UserId == user.Id);
            if (membership is null) db.AcademyMemberships.Add(new AcademyMembership { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = user.Id, Role = AcademyRole.Guardian, IsActive = true, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
            else if (membership.Role != AcademyRole.Guardian || !membership.IsActive) return Results.Conflict(new { message = "الحساب لا يملك عضوية ولي أمر فعالة." });
            guardian = await db.GuardianProfiles.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.UserId == user.Id) ?? new GuardianProfile { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = user.Id, DisplayName = request.GuardianName.Trim(), ContactPhone = phone, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            if (db.Entry(guardian).State == EntityState.Detached) db.GuardianProfiles.Add(guardian);
        }
        if (guardian is null) return Results.NotFound();
        var guardianLinkExists = await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.GuardianId == guardian.Id && x.PlayerId == player.Id && x.IsActive);
        if (!guardianLinkExists)
            db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GuardianId = guardian.Id, PlayerId = player.Id, RelationshipType = request.RelationshipType ?? "ولي أمر", CreatedByUserId = actorId, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        db.SportEnrollments.Add(new SportEnrollment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player.Id, SportId = group.SportId, BranchId = group.BranchId, TrainingGroupId = group.Id, Status = EnrollmentStatus.Active, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Results.Created($"/api/v1/people/players/{player.Id}", new { playerId = player.Id, player.PlayerCode, subscriptionCreated = false });
    }
}

public sealed record NamedRequest(string ArabicName, string? EnglishName, string? Description);
public sealed record AgeCategoryRequest(string ArabicName, int? MinimumBirthYear, int? MaximumBirthYear);
public sealed record GroupRequest(string ArabicName, Guid BranchId, Guid SportId, Guid AgeCategoryId, int? Capacity);
public sealed record ScheduleRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record CoachAssignmentRequest(Guid MembershipId);
public sealed record RegistrationRequest(Guid? ExistingPlayerId, string? PlayerArabicName, DateOnly? DateOfBirth, Gender? Gender, Guid? ExistingGuardianId, string? GuardianName, string? GuardianPhone, string? RelationshipType, Guid BranchId, Guid SportId, Guid GroupId);
