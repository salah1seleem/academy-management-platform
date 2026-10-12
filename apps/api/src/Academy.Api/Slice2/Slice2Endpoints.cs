using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Academy.Api.Slice3;
using Academy.Api.Operations;
using Academy.Api.Finance;
using Academy.Infrastructure.Subscriptions;
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
            var categories = await db.AgeCategories.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.MinimumBirthYear, x.MaximumBirthYear }).ToListAsync();
            var groups = await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive).OrderBy(x => x.ArabicName)
                .Select(x => new { x.Id, x.ArabicName, x.BranchId, x.SportId, x.AgeCategoryId, x.TrainingLevelId, trainingLevel = db.TrainingLevels.Where(l => l.Id == x.TrainingLevelId).Select(l => l.ArabicName).FirstOrDefault(), registrationFormat = x.RegistrationFormat.ToString() }).ToListAsync();
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
        people.MapGet("/guardians", async (string? search, CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; var normalizedPhone = EgyptPhoneNormalizer.Normalize(search); return Results.Ok(await db.GuardianProfiles.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive && (string.IsNullOrWhiteSpace(search) || x.DisplayName.Contains(search.Trim()) || (normalizedPhone != null && x.ContactPhone == normalizedPhone))).OrderBy(x => x.DisplayName).Select(x => new { x.Id, x.DisplayName, x.ContactPhone }).ToListAsync()); });
        people.MapPost("/registrations", RegisterPlayer).AddEndpointFilter<CsrfFilter>();
        people.MapGet("/registration-cash-options", async (Guid branchId, Guid planId, CashRegisterService cash) => {
            try { return Results.Ok(await cash.RegistrationOptions(branchId, planId)); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        });

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
            var groups = await db.StaffGroupAssignments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.AcademyMembership.UserId == userId && x.IsActive && x.TrainingGroup.IsActive && x.AcademyMembership.IsActive).Select(x => new { x.TrainingGroup.Id, x.TrainingGroup.ArabicName }).ToListAsync(); return Results.Ok(groups);
        }).RequireAuthorization(AcademyPermissions.CoachGroupsRead);
    }

    private static async Task<IResult> SearchPlayers(string? search, Guid? branchId, Guid? sportId, Guid? ageCategoryId, Guid? groupId, string? subscriptionState, DateOnly? asOf, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var date = asOf ?? clock.Today;
        var groupDate = asOf ?? await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (subscriptionState is not (null or "" or "active" or "expiring" or "expired" or "subscribed" or "stable" or "other")) return Results.UnprocessableEntity();
        var query = db.Players.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.ArabicName.Contains(search) || x.PlayerCode.Contains(search) || db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.Id && l.Guardian.ContactPhone == EgyptPhoneNormalizer.Normalize(search)));
        if (branchId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.BranchId == branchId));
        if (sportId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.SportId == sportId));
        if (ageCategoryId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && db.TrainingGroups.Any(g => g.Id == FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, groupDate) && g.AgeCategoryId == ageCategoryId)));
        if (groupId.HasValue) query = query.Where(x => db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, groupDate) == groupId));
        var eligible = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollment.IsActive && p.SportEnrollment.Player.IsActive && (!branchId.HasValue || p.SportEnrollment.BranchId == branchId) && (!sportId.HasValue || p.SportEnrollment.SportId == sportId) && (!groupId.HasValue || FoundationDbContext.AssignedGroupAt(p.AcademyId, p.SportEnrollmentId, groupDate) == groupId) && p.Status != SubscriptionPeriodStatus.Cancelled && p.Status != SubscriptionPeriodStatus.Frozen);
        var subscribed = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollment.IsActive && p.SportEnrollment.Player.IsActive && (!branchId.HasValue || p.SportEnrollment.BranchId == branchId) && (!sportId.HasValue || p.SportEnrollment.SportId == sportId) && (!groupId.HasValue || FoundationDbContext.AssignedGroupAt(p.AcademyId, p.SportEnrollmentId, groupDate) == groupId) && p.Status != SubscriptionPeriodStatus.Cancelled);
        var activePeriods = eligible.Where(p => p.StartDate <= date && (p.EndDate == null || p.EndDate >= date) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0));
        var expiredPeriods = eligible.Where(p => p.EndDate < date || (p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0);
        if (subscriptionState == "subscribed") query = query.Where(x => subscribed.Any(p => p.SportEnrollment.PlayerId == x.Id));
        if (subscriptionState == "stable") query = query.Where(x => activePeriods.Any(p => p.SportEnrollment.PlayerId == x.Id) && !activePeriods.Any(p => p.SportEnrollment.PlayerId == x.Id && p.EndDate <= date.AddDays(7)));
        if (subscriptionState == "other") query = query.Where(x => subscribed.Any(p => p.SportEnrollment.PlayerId == x.Id) && !activePeriods.Any(p => p.SportEnrollment.PlayerId == x.Id) && !expiredPeriods.Any(p => p.SportEnrollment.PlayerId == x.Id));
        if (subscriptionState == "active") query = query.Where(x => eligible.Any(p => p.SportEnrollment.PlayerId == x.Id && p.StartDate <= date && (p.EndDate == null || p.EndDate >= date) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)));
        if (subscriptionState == "expiring") query = query.Where(x => eligible.Any(p => p.SportEnrollment.PlayerId == x.Id && p.StartDate <= date && p.EndDate >= date && p.EndDate <= date.AddDays(7) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)));
        if (subscriptionState == "expired") query = query.Where(x => eligible.Any(p => p.SportEnrollment.PlayerId == x.Id && (p.EndDate < date || (p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0)) && !eligible.Any(p => p.SportEnrollment.PlayerId == x.Id && p.StartDate <= date && (p.EndDate == null || p.EndDate >= date) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)));
        return Results.Ok(await query.OrderBy(x => x.ArabicName).Select(x => new
        {
            x.Id, x.PlayerCode, x.ArabicName, x.DateOfBirth, x.IsActive, x.PhotoReference,
            hasActiveSubscription = eligible.Any(p => p.SportEnrollment.PlayerId == x.Id && p.StartDate <= date && (p.EndDate == null || p.EndDate >= date) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)),
            enrollments = db.SportEnrollments.Count(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.Status == EnrollmentStatus.Active),
            enrollmentSummaries = db.SportEnrollments.Where(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id).OrderBy(e => e.Sport.ArabicName).ThenBy(e => e.Id).Select(e => new
            {
                e.Id, sportName = e.Sport.ArabicName, branchName = e.Branch.ArabicName, groupName = db.TrainingGroups.Where(g => g.Id == FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, groupDate)).Select(g => g.ArabicName).FirstOrDefault() ?? "في انتظار المجموعة", status = e.Status.ToString(),
                subscription = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == e.Id).OrderByDescending(p => p.UpdatedAtUtc)
                    .Select(p => new { plan = (p.PlanNameSnapshot ?? p.SubscriptionPlan.ArabicName), status = p.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : p.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : p.EndDate < date || ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0) ? "Expired" : p.StartDate > date ? "Scheduled" : "Active" }).FirstOrDefault()
            }).ToList()
        }).ToListAsync());
    }

    private static async Task<IResult> GuardianChildren(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var children = await db.GuardianPlayerLinks.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == userId && x.IsActive && x.Player.IsActive).OrderBy(x => x.Player.ArabicName).Select(x => new { x.Player.Id, x.Player.PlayerCode, x.Player.ArabicName }).ToListAsync();
        return Results.Ok(children);
    }

    private static async Task<IResult> RegisterPlayer(RegistrationRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, UserManager<ApplicationUser> users, ISubscriptionClock clock, CollectionService collections, DiscountRules discounts, Academy.Api.Operations.GroupPlacementService placement, TimeProvider time, CashRegisterService cash)
    {
        var t = (await tenant.ResolveAsync())!; var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (request.PaymentMethod == "Cash" && !request.PlanId.HasValue)
            return Results.Problem(statusCode: 422, title: "القبض النقدي يحتاج باقة ومراجعة مبلغ.");
        if (request.PlanId.HasValue)
        {
            try { await cash.AuthorizeRegistration(); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }
        var group = await db.TrainingGroups.AsNoTracking().Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.GroupId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive);
        if (group is null || group.BranchId != request.BranchId || group.SportId != request.SportId) return Results.UnprocessableEntity(new { message = "المجموعة لا تطابق الفرع والرياضة المختارين." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        var payloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(request))));
        if (request.PlanId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || request.PaymentMethod is not ("Cash" or "Online")) return Results.UnprocessableEntity(new { message = "أكد طريقة التحصيل وأعد إرسال الطلب." });
            key = "register-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"registration:{t.AcademyId}:{actorId}:{key}"}, 0))");
            var prior = await db.RenewalRequests.Where(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == actorId && x.IdempotencyKey == key).Select(x => new { playerId = x.SportEnrollment.PlayerId, x.RegistrationPayloadHash, x.PaymentRequestId }).SingleOrDefaultAsync();
            if (prior is not null)
            {
                if (prior.RegistrationPayloadHash != payloadHash) return Results.Conflict(new { message = "بيانات الطلب تغيرت. استخدم عملية تسجيل جديدة." });
                if (request.PaymentMethod == "Cash" && !await db.CashReceiptPostings.AnyAsync(p => db.Collections.Any(c => c.Id == p.CollectionId && c.PaymentRequestId == prior.PaymentRequestId)))
                    return Results.Conflict(new { message = "التسجيل النقدي التاريخي يحتاج مطابقة مستقلة؛ لم تُنشأ حركة صندوق جديدة." });
                return Results.Ok(new { prior.playerId, subscriptionCreated = true, replay = true });
            }
            await PlanCatalog.LockAsync(db, t.AcademyId, request.PlanId.Value);
            if (!await db.SubscriptionPlans.TrainingOnly().ForRegistrationFormat(group.RegistrationFormat).ForBranch(group.BranchId).AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlanId && x.SportId == group.SportId && x.IsActive)) return Results.UnprocessableEntity(new { message = "اختر باقة فعالة مطابقة للرياضة والفرع." });
            if (request.PaymentMethod == "Cash")
            {
                try { await cash.PrepareRegistration(request.CashSessionId, group.BranchId, request.PlanId.Value, request.ExpectedAmount); }
                catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            }
        }
        if (group.RegistrationFormat != TrainingRegistrationFormat.Group)
        {
            try { group = await placement.PrepareSpecialAdmission(group.Id); }
            catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            if (!request.PlanId.HasValue || !request.LevelReviewed || !request.TrainingLevelId.HasValue || request.TrainingLevelId != group.TrainingLevelId) return Results.UnprocessableEntity(new { message = "التدريب الخاص وشبه الخاص يحتاج باقة مطابقة ومراجعة صريحة لمستوى اللاعب قبل التحصيل." });
        }
        Player player;
        if (request.ExistingPlayerId is Guid playerId)
        {
            player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId && x.IsActive) ?? null!;
            if (player is null) return Results.NotFound();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.PlayerArabicName) || request.DateOfBirth is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["player"] = ["اسم اللاعب وتاريخ الميلاد مطلوبان."] });
            player = new Player { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerCode = $"PLR-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), ArabicName = request.PlayerArabicName.Trim(), DateOfBirth = request.DateOfBirth.Value, Gender = request.Gender, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        }
        if (group.AgeCategory.MinimumBirthYear > player.DateOfBirth.Year || group.AgeCategory.MaximumBirthYear < player.DateOfBirth.Year || player.DateOfBirth > DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)) return Results.UnprocessableEntity(new { message = "عمر اللاعب غير مناسب للمجموعة." });
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{group.Id}"}, 0))");
        if (!await DatedGroupRoster.CanAdd(db, group.Id, await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time), group.Capacity)) return Results.Conflict(new { message = "المجموعة مكتملة العدد في الفترة الحالية أو في نقل مجدول." });
        if (await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == player.Id && x.SportId == request.SportId && x.RegistrationFormat == group.RegistrationFormat && x.BranchId == group.BranchId && x.Status == EnrollmentStatus.Active)) return Results.Conflict(new { message = "للاعب تسجيل نشط في هذه الرياضة بالفعل." });
        GuardianProfile guardian;
        if (request.ExistingGuardianId is Guid guardianId)
            guardian = await db.GuardianProfiles.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == guardianId && x.IsActive) ?? null!;
        else
        {
            var phone = EgyptPhoneNormalizer.Normalize(request.GuardianPhone);
            if (phone is null || string.IsNullOrWhiteSpace(request.GuardianName)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["guardian"] = ["اسم ولي الأمر ورقم هاتف صحيح مطلوبان."] });
            var user = await users.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone);
            if (user is null) { user = new ApplicationUser { Id = Guid.NewGuid(), UserName = phone, PhoneNumber = phone, PhoneNumberConfirmed = false, DisplayName = request.GuardianName.Trim(), IsActive = true, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow }; var result = await users.CreateAsync(user); if (!result.Succeeded) return Results.ValidationProblem(new Dictionary<string, string[]> { ["guardian"] = result.Errors.Select(x => x.Description).ToArray() }); }
            var membership = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.UserId == user.Id && x.Role == AcademyRole.Guardian);
            if (membership is null) db.AcademyMemberships.Add(new AcademyMembership { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = user.Id, Role = AcademyRole.Guardian, IsActive = true, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
            else if (membership.Role != AcademyRole.Guardian || !membership.IsActive) return Results.Conflict(new { message = "الحساب لا يملك عضوية ولي أمر فعالة." });
            guardian = await db.GuardianProfiles.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.UserId == user.Id) ?? new GuardianProfile { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = user.Id, DisplayName = request.GuardianName.Trim(), ContactPhone = phone, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
            if (db.Entry(guardian).State == EntityState.Detached) db.GuardianProfiles.Add(guardian);
        }
        if (guardian is null) return Results.NotFound();
        // Identity may save its own user while provisioning a new guardian. Keep
        // the new player detached until its branch-scoped enrollment/link graph
        // is ready, so a branch administrator never flushes an orphan player.
        if (!request.ExistingPlayerId.HasValue) db.Players.Add(player);
        var guardianLinkExists = await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.GuardianId == guardian.Id && x.PlayerId == player.Id && x.IsActive);
        if (!guardianLinkExists)
            db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GuardianId = guardian.Id, PlayerId = player.Id, RelationshipType = request.RelationshipType ?? "ولي أمر", CreatedByUserId = actorId, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player.Id, SportId = group.SportId, BranchId = group.BranchId, TrainingGroupId = group.RegistrationFormat == TrainingRegistrationFormat.Group ? group.Id : null, RegistrationFormat = group.RegistrationFormat, TrainingLevelId = group.RegistrationFormat != TrainingRegistrationFormat.Group ? request.TrainingLevelId : null, LevelReviewedByUserId = group.RegistrationFormat != TrainingRegistrationFormat.Group ? actorId : null, LevelReviewedAtUtc = group.RegistrationFormat != TrainingRegistrationFormat.Group ? clock.UtcNow : null, Status = EnrollmentStatus.Active, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.SportEnrollments.Add(enrollment);
        if (request.DiscountRuleId is Guid ruleId) {
            if (!await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == ruleId && x.IsActive && !x.IsSiblingOffer)) return Results.NotFound();
            if (!await db.PlayerDiscountLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == player.Id && x.DiscountRuleId == ruleId)) db.PlayerDiscountLinks.Add(new Academy.Infrastructure.Operations.PlayerDiscountLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player.Id, DiscountRuleId = ruleId, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        }
        await db.SaveChangesAsync();
        if (request.PlanId.HasValue && request.ExpectedAmount.HasValue) {
            var plan = await db.SubscriptionPlans.TrainingOnly().SingleAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlanId);
            var quote = await discounts.CalculateAsync(t.AcademyId, player.Id, plan.Price, actorId, plan: plan, branchId: enrollment.BranchId);
            if (quote.FinalAmount != request.ExpectedAmount.Value) return Results.Conflict(new { message = "تغير المبلغ المستحق. راجع السعر قبل التحصيل." });
        }
        if (request.PlanId.HasValue)
        {
            var paymentId = await collections.CreateCashAsync(t.AcademyId, actorId, enrollment.Id, request.PlanId.Value, key, request.PaymentMethod!, registrationPayloadHash: payloadHash);
            if (request.PaymentMethod == "Cash")
            {
                try { await cash.PostRegistration(request.CashSessionId!.Value, paymentId, key, payloadHash); }
                catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            }
        }
        if (group.RegistrationFormat != TrainingRegistrationFormat.Group)
        {
            try { await placement.FinishSpecialAdmission(group, enrollment); }
            catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }
        await transaction.CommitAsync();
        return Results.Created($"/api/v1/people/players/{player.Id}", new { playerId = player.Id, player.PlayerCode, subscriptionCreated = request.PlanId.HasValue });
    }
}

public sealed record NamedRequest(string ArabicName, string? EnglishName, string? Description);
public sealed record AgeCategoryRequest(string ArabicName, int? MinimumBirthYear, int? MaximumBirthYear);
public sealed record GroupRequest(string ArabicName, Guid BranchId, Guid SportId, Guid AgeCategoryId, int? Capacity);
public sealed record ScheduleRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record CoachAssignmentRequest(Guid MembershipId);
public sealed record RegistrationRequest(Guid? ExistingPlayerId, string? PlayerArabicName, DateOnly? DateOfBirth, Gender? Gender, Guid? ExistingGuardianId, string? GuardianName, string? GuardianPhone, string? RelationshipType, Guid BranchId, Guid SportId, Guid GroupId, Guid? PlanId = null, string? PaymentMethod = null, Guid? DiscountRuleId = null,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal? ExpectedAmount = null, Guid? TrainingLevelId = null, bool LevelReviewed = false, Guid? CashSessionId = null);
