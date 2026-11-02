using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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

        manage.MapGet("/structure/options", async (CurrentTenant tenant, FoundationDbContext db, TimeProvider time) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var groupDay=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
            var branches = await db.Branches.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName }).ToListAsync();
            var sports = await db.Sports.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName }).ToListAsync();
            var categories = await db.AgeCategories.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.MinimumBirthYear, x.MaximumBirthYear }).ToListAsync();
            var groups = await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive).OrderBy(x => x.ArabicName)
                .Select(x => new { x.Id, x.ArabicName, x.BranchId, x.SportId, x.AgeCategoryId, x.TrainingLevelId,x.Capacity,branchName=x.Branch.ArabicName,sportName=x.Sport.ArabicName,playerCount=db.SportEnrollments.Count(e=>e.AcademyId==t.AcademyId&&e.IsActive&&FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,groupDay)==x.Id),coachIds=db.StaffGroupAssignments.Where(a=>a.TrainingGroupId==x.Id&&a.IsActive&&a.AcademyMembership.IsActive&&a.AcademyMembership.User.IsActive).OrderBy(a=>a.AcademyMembership.User.DisplayName).Select(a=>a.AcademyMembershipId).ToArray(),coaches=db.StaffGroupAssignments.Where(a=>a.AcademyId==t.AcademyId&&a.TrainingGroupId==x.Id&&a.IsActive&&a.AcademyMembership.IsActive).Select(a=>a.AcademyMembership.User.DisplayName).ToArray(), trainingLevel = db.TrainingLevels.Where(l => l.Id == x.TrainingLevelId).Select(l => l.ArabicName).FirstOrDefault(), registrationFormat = x.RegistrationFormat.ToString() }).ToListAsync();
            var coaches = await db.AcademyMemberships.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Role == AcademyRole.Coach && x.IsActive&&x.User.IsActive).OrderBy(x => x.User.DisplayName).Select(x => new { id = x.Id, arabicName = x.User.DisplayName,x.AllBranches,x.AllSports,branchIds=db.MembershipBranchAccesses.Where(a=>a.MembershipId==x.Id&&a.IsActive).Select(a=>a.BranchId).ToArray(),sportIds=db.MembershipSportAccesses.Where(a=>a.MembershipId==x.Id&&a.IsActive).Select(a=>a.SportId).ToArray() }).ToListAsync();
            return Results.Ok(new { branches, sports, categories, groups, coaches });
        });

        manage.MapPost("/branches", async (NamedRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var normalizedName = System.Text.RegularExpressions.Regex.Replace(request.ArabicName?.Trim() ?? "", @"\s+", " ");
            if(string.IsNullOrWhiteSpace(request.ArabicName)||normalizedName.Length>120||request.Description?.Length>240)return Results.UnprocessableEntity(new{message="راجع اسم الفرع والعنوان."});
            if(await db.Branches.AnyAsync(b=>b.AcademyId==t.AcademyId&&b.ArabicName==normalizedName))return Results.Conflict(new{code="structure_name_exists",message="يوجد فرع بهذا الاسم."});
            var entity = new Branch { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = normalizedName, EnglishName = request.EnglishName, Address = request.Description, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.Branches.Add(entity); try { await db.SaveChangesAsync(); } catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(new{message="يوجد فرع بهذا الاسم."}); } return Results.Created($"/api/v1/manage/branches/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/sports", async (NamedRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var normalizedName = System.Text.RegularExpressions.Regex.Replace(request.ArabicName?.Trim() ?? "", @"\s+", " ");
            if(string.IsNullOrWhiteSpace(request.ArabicName)||normalizedName.Length>120||request.Kind.HasValue&&!Enum.IsDefined(request.Kind.Value))return Results.UnprocessableEntity(new{message="راجع اسم الرياضة ونوعها."});
            if(await db.Sports.AnyAsync(b=>b.AcademyId==t.AcademyId&&b.ArabicName==normalizedName))return Results.Conflict(new{code="structure_name_exists",message="توجد رياضة بهذا الاسم."});
            var entity = new Sport { Id = Guid.NewGuid(), AcademyId = t.AcademyId, Kind=request.Kind??(normalizedName.Contains("قدم")?SportKind.Football:normalizedName.Contains("سباح")?SportKind.Swimming:SportKind.General), ArabicName = normalizedName, EnglishName = request.EnglishName, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.Sports.Add(entity); try { await db.SaveChangesAsync(); } catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(new{message="توجد رياضة بهذا الاسم."}); } return Results.Created($"/api/v1/manage/sports/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/age-categories", async (AgeCategoryRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!;
            if(string.IsNullOrWhiteSpace(request.ArabicName)||request.ArabicName.Length>120||request.MinimumBirthYear<1900||request.MaximumBirthYear>2100||request.MinimumBirthYear>request.MaximumBirthYear)return Results.UnprocessableEntity(new{message="راجع اسم الفئة وسنوات الميلاد."});
            var entity = new AgeCategory { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = request.ArabicName.Trim(), MinimumBirthYear = request.MinimumBirthYear, MaximumBirthYear = request.MaximumBirthYear, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
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
        // Narrow resource read for confirmation; does not require the shared coach profile.
        manage.MapGet("/groups/{groupId:guid}/coaches/{membershipId:guid}", async (Guid groupId, Guid membershipId, CurrentTenant tenant, FoundationDbContext db) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var assignment = await db.StaffGroupAssignments.AsNoTracking().Where(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == groupId && a.AcademyMembershipId == membershipId && a.IsActive && a.TrainingGroup.IsActive && a.AcademyMembership.IsActive && a.AcademyMembership.Role == AcademyRole.Coach)
                .Select(a => new { a.Id, a.TrainingGroupId, a.AcademyMembershipId, a.IsActive, groupName = a.TrainingGroup.ArabicName, coachName = a.AcademyMembership.User.DisplayName }).SingleOrDefaultAsync();
            return assignment is null ? Results.NotFound() : Results.Ok(assignment);
        });
        manage.MapPost("/groups/{groupId:guid}/coaches", async (Guid groupId, CoachAssignmentRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) =>
        {
            var t = (await tenant.ResolveAsync())!;
            await using var transaction=await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"coach-assignment:{t.AcademyId}:{groupId}:{request.MembershipId}"}, 0))");
            var groupExists = await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId && x.IsActive);
            var coach = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.MembershipId && x.Role == AcademyRole.Coach && x.IsActive);
            if (!groupExists || coach is null) return Results.UnprocessableEntity(new { message = "المدرب أو المجموعة خارج نطاق الأكاديمية." });
            var existing = await db.StaffGroupAssignments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == groupId && x.AcademyMembershipId == coach.Id);
            if (existing is not null)
            {
                if (!existing.IsActive)
                {
                    existing.IsActive = true; existing.UpdatedAtUtc = clock.GetUtcNow();
                    await db.SaveChangesAsync();
                }
                await transaction.CommitAsync(); return Results.Ok(existing.Id);
            }
            var entity = new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, AcademyMembershipId = coach.Id, TrainingGroupId = groupId, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            db.StaffGroupAssignments.Add(entity); await db.SaveChangesAsync();await transaction.CommitAsync(); return Results.Created($"/api/v1/manage/group-assignments/{entity.Id}", entity.Id);
        }).AddEndpointFilter<CsrfFilter>();

        var people = api.MapGroup("/people").RequireAuthorization(AcademyPermissions.PeopleManage);
        people.MapGet("/players", SearchPlayers);
        people.MapGet("/guardians", MemberSubscriptionValues.GuardianList);
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
        api.MapGet("/coach/groups", async (CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider time) =>
        {
            var t = (await tenant.ResolveAsync())!; var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var day=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
            var groups = await db.StaffGroupAssignments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.AcademyMembership.UserId == userId && x.IsActive && x.TrainingGroup.IsActive && x.AcademyMembership.IsActive).Select(x => new { x.TrainingGroup.Id, x.TrainingGroup.ArabicName,branchName=x.TrainingGroup.Branch.ArabicName,sportName=x.TrainingGroup.Sport.ArabicName,playerCount=db.SportEnrollments.Count(e=>e.AcademyId==t.AcademyId&&e.IsActive&&FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,day)==x.TrainingGroupId),recurring=db.RecurringSchedules.Where(s=>s.TrainingGroupId==x.TrainingGroupId&&s.IsActive).Select(s=>new{s.DayOfWeek,s.StartTime,s.EndTime,s.ValidFrom,s.ValidTo}).ToList(),coaches=db.StaffGroupAssignments.Where(a=>a.AcademyId==t.AcademyId&&a.TrainingGroupId==x.TrainingGroupId&&a.IsActive&&a.AcademyMembership.IsActive).Select(a=>a.AcademyMembership.User.DisplayName).ToArray() }).ToListAsync(); return Results.Ok(groups);
        }).RequireAuthorization(AcademyPermissions.CoachGroupsRead);
    }

    private static async Task<IResult> SearchPlayers(string? search, Guid? branchId, Guid? sportId, Guid? ageCategoryId, Guid? groupId, string? subscriptionState, DateOnly? asOf, string? sort, int? page, int? pageSize, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, TimeProvider time, ClaimsPrincipal actor, IAuthorizationService authorization)
    {
        var t = (await tenant.ResolveAsync())!; var date = asOf ?? clock.Today;
        var financialValuesVisible=await MemberSubscriptionValues.FinancialValuesVisible(t,actor,authorization,db);
        if(!financialValuesVisible&&sort is "value_asc" or "value_desc")return Results.Problem(statusCode:403,title:"الترتيب بالقيم المالية يحتاج صلاحية التقارير المالية.");
        if (sort is not (null or "" or "name" or "value_asc" or "value_desc") || page is < 1 or > 1000000 || pageSize is < 1 or > 100) return Results.UnprocessableEntity(new { message = "اختر ترتيبًا وصفحة صحيحين." });
        var currency = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.DefaultCurrency).SingleAsync();
        var values = MemberSubscriptionValues.Active(db, t.AcademyId, date, currency, branchId, sportId);
        var groupDate = asOf ?? await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (subscriptionState is not (null or "" or "active" or "expiring" or "expired" or "subscribed" or "stable" or "other")) return Results.UnprocessableEntity();
        var query = db.Players.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var phone = EgyptPhoneNormalizer.Normalize(term);
            query = query.Where(x => x.ArabicName.Contains(term) || x.PlayerCode.Contains(term) ||
                (phone != null && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.Id && l.IsActive && l.Guardian.IsActive && l.Guardian.ContactPhone == phone)));
        }
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
        var projected = query.Select(x => new
        {
            x.Id, x.PlayerCode, x.ArabicName, x.DateOfBirth, x.Gender, x.IsActive, x.PhotoReference,
            monthlySubscriptionValue = financialValuesVisible ? (decimal?)Math.Round(values.Where(v => v.PlayerId == x.Id).Sum(v => v.MonthlyValue) ?? 0, 2) : null,
            unpricedSubscriptions = financialValuesVisible ? (int?)values.Count(v => v.PlayerId == x.Id && v.MonthlyValue == null) : null, currency,
            hasActiveSubscription = eligible.Any(p => p.SportEnrollment.PlayerId == x.Id && p.StartDate <= date && (p.EndDate == null || p.EndDate >= date) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)),
            enrollments = db.SportEnrollments.Count(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id && e.Status == EnrollmentStatus.Active),
            enrollmentSummaries = db.SportEnrollments.Where(e => e.AcademyId == t.AcademyId && e.PlayerId == x.Id).OrderBy(e => e.Sport.ArabicName).ThenBy(e => e.Id).Select(e => new
            {
                e.Id,e.SportId,e.BranchId,criteriaReady=db.EvaluationCriteria.Any(c=>c.AcademyId==t.AcademyId&&c.SportId==e.SportId&&c.IsActive),groupId=FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,groupDate), sportName = e.Sport.ArabicName, branchName = e.Branch.ArabicName, groupName = db.TrainingGroups.Where(g => g.Id == FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, groupDate)).Select(g => g.ArabicName).FirstOrDefault() ?? "في انتظار المجموعة", status = e.Status.ToString(),
                subscription = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == e.Id).OrderByDescending(p => p.UpdatedAtUtc)
                    .Select(p => new {p.RemainingSessions,p.EndDate,p.StartDate,sessionBased=(p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType)!=SubscriptionPlanType.Duration, plan = (p.PlanNameSnapshot ?? p.SubscriptionPlan.ArabicName), status = p.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : p.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : p.EndDate < date || ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0) ? "Expired" : p.StartDate > date ? "Scheduled" : "Active" }).FirstOrDefault()
            }).ToList()
        });
        var ordered = sort switch {
            "value_desc" => projected.OrderBy(x => x.unpricedSubscriptions > 0).ThenByDescending(x => x.monthlySubscriptionValue).ThenBy(x => x.ArabicName).ThenBy(x => x.Id),
            "value_asc" => projected.OrderBy(x => x.unpricedSubscriptions > 0).ThenBy(x => x.monthlySubscriptionValue).ThenBy(x => x.ArabicName).ThenBy(x => x.Id),
            _ => projected.OrderBy(x => x.ArabicName).ThenBy(x => x.Id)
        };
        if (!page.HasValue && !pageSize.HasValue) return Results.Ok(await ordered.ToArrayAsync());
        var size = pageSize ?? 25; var number = page ?? 1;
        return Results.Ok(new { items = await ordered.Skip((number - 1) * size).Take(size).ToArrayAsync(), total = await query.CountAsync(), page = number, pageSize = size, currency, asOf = date, financialValuesVisible, basis = financialValuesVisible?MemberSubscriptionValues.Basis:null });
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
        if (request.ExceptionalDiscount is not null && (!request.PlanId.HasValue || request.ExpectedAmount is null)) return Results.Problem(statusCode: 422, title: "الخصم الاستثنائي يحتاج باقة ومراجعة السعر النهائي.");
        if (request.ExceptionalDiscount is not null && !await ManualEnrollmentDiscount.Allowed(db, t)) return Results.Forbid();
        if (request.PaymentMethod == "Cash" && !request.PlanId.HasValue)
            return Results.Problem(statusCode: 422, title: "القبض النقدي يحتاج باقة ومراجعة مبلغ.");
        if (request.PlanId.HasValue)
        {
            try { await cash.AuthorizeRegistration(); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }
        var group = await db.TrainingGroups.AsNoTracking().Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.GroupId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive);
        if (request.GroupId.HasValue && (group is null || group.BranchId != request.BranchId || group.SportId != request.SportId)) return Results.UnprocessableEntity(new { code="registration_group_scope", message = "المجموعة لا تطابق الفرع والرياضة المختارين." });
        if (!await db.Branches.AnyAsync(b=>b.Id==request.BranchId&&b.AcademyId==t.AcademyId&&b.IsActive)||!await db.Sports.AnyAsync(s=>s.Id==request.SportId&&s.AcademyId==t.AcademyId&&s.IsActive))return Results.NotFound();
        var format=group?.RegistrationFormat??TrainingRegistrationFormat.Group;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        if (request.ExceptionalDiscount is not null && !await ManualEnrollmentDiscount.Allowed(db, t)) return Results.Forbid();
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
                if (prior.PaymentRequestId.HasValue && request.PaymentMethod == "Cash" && !await db.CashReceiptPostings.AnyAsync(p => db.Collections.Any(c => c.Id == p.CollectionId && c.PaymentRequestId == prior.PaymentRequestId)))
                    return Results.Conflict(new { message = "التسجيل النقدي التاريخي يحتاج مطابقة مستقلة؛ لم تُنشأ حركة صندوق جديدة." });
                return Results.Ok(await RegistrationRead(db,t.AcademyId,actorId,key,prior.playerId,null,true,true));
            }
            await PlanCatalog.LockAsync(db, t.AcademyId, request.PlanId.Value);
            if (!await db.SubscriptionPlans.TrainingOnly().ForRegistrationFormat(format).ForBranch(request.BranchId).AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlanId && x.SportId == request.SportId && x.IsActive)) return Results.UnprocessableEntity(new { message = "اختر باقة فعالة مطابقة للرياضة والفرع." });
            if (request.PaymentMethod == "Cash" && request.ExpectedAmount != 0)
            {
                try { await cash.PrepareRegistration(request.CashSessionId, request.BranchId, request.PlanId.Value, request.ExpectedAmount); }
                catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            }
        }
        if (format != TrainingRegistrationFormat.Group)
        {
            try { group = await placement.PrepareSpecialAdmission(group!.Id); }
            catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            if (!request.PlanId.HasValue || !request.LevelReviewed || !request.TrainingLevelId.HasValue || request.TrainingLevelId != group!.TrainingLevelId) return Results.UnprocessableEntity(new { message = "التدريب الخاص وشبه الخاص يحتاج باقة مطابقة ومراجعة صريحة لمستوى اللاعب قبل التحصيل." });
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
        if (player.DateOfBirth > DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)) return Results.UnprocessableEntity(new {code="registration_birth_date", message="تاريخ الميلاد لا يمكن أن يكون في المستقبل."});
        if (group is not null)
        {
            if (!request.AgeOverrideConfirmed && (group.AgeCategory.MinimumBirthYear > player.DateOfBirth.Year || group.AgeCategory.MaximumBirthYear < player.DateOfBirth.Year)) return Results.UnprocessableEntity(new {code="registration_group_age", message="عمر اللاعب غير مناسب للمجموعة.",minimumBirthYear=group.AgeCategory.MinimumBirthYear,maximumBirthYear=group.AgeCategory.MaximumBirthYear});
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{group.Id}"}, 0))");
            if (!await DatedGroupRoster.CanAdd(db, group.Id, await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time), group.Capacity)) return Results.Conflict(new { message = "المجموعة مكتملة العدد في الفترة الحالية أو في نقل مجدول." });
        }
        if (await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == player.Id && x.SportId == request.SportId && x.RegistrationFormat == format && x.BranchId == request.BranchId && x.Status == EnrollmentStatus.Active)) return Results.Conflict(new { message = "للاعب تسجيل نشط في هذه الرياضة بالفعل." });
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
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player.Id, SportId = request.SportId, BranchId = request.BranchId, TrainingGroupId = format == TrainingRegistrationFormat.Group ? group?.Id : null, RegistrationFormat = format, TrainingLevelId = format != TrainingRegistrationFormat.Group ? request.TrainingLevelId : null, LevelReviewedByUserId = format != TrainingRegistrationFormat.Group ? actorId : null, LevelReviewedAtUtc = format != TrainingRegistrationFormat.Group ? clock.UtcNow : null, Status = EnrollmentStatus.Active, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.SportEnrollments.Add(enrollment);
        if(group is not null && request.AgeOverrideConfirmed && (group.AgeCategory.MinimumBirthYear > player.DateOfBirth.Year || group.AgeCategory.MaximumBirthYear < player.DateOfBirth.Year))
            db.AcademyPolicyAudits.Add(new Academy.Infrastructure.Operations.AcademyPolicyAudit{Id=enrollment.Id,AcademyId=t.AcademyId,ActorUserId=actorId,Policy="تأكيد تسجيل خارج الفئة العمرية",Before=false,After=true,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow});
        if (request.DiscountRuleId is Guid ruleId) {
            if (!await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == ruleId && x.IsActive && !x.IsSiblingOffer&&!db.VipRewards.Any(v=>v.DiscountRuleId==x.Id))) return Results.NotFound();
            if (!await db.PlayerDiscountLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == player.Id && x.DiscountRuleId == ruleId)) db.PlayerDiscountLinks.Add(new Academy.Infrastructure.Operations.PlayerDiscountLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player.Id, DiscountRuleId = ruleId, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        }
        await db.SaveChangesAsync();
        RenewalFinancialSnapshot? registrationPrice = null;
        RenewalFinancialSnapshot? previousDiscount = null;
        if (request.PlanId.HasValue) {
            var plan = await db.SubscriptionPlans.TrainingOnly().SingleAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlanId);
            var quote = await discounts.CalculateAsync(t.AcademyId, player.Id, plan.Price, actorId, plan: plan, branchId: enrollment.BranchId);
            if (request.ExceptionalDiscount is not null) {
                try { previousDiscount = quote; quote = ManualEnrollmentDiscount.Apply(quote, request.ExceptionalDiscount, actorId, clock.UtcNow); }
                catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
                if (quote.FinalAmount == 0 && t.Role != AcademyRole.AcademyOwner) return Results.Problem(statusCode: 403, title: "الإعفاء الكامل من السداد يحتاج صلاحية المالك وفق السياسة الحالية.");
            }
            registrationPrice = quote;
            if (request.ExpectedAmount.HasValue && quote.FinalAmount != request.ExpectedAmount.Value) return Results.Conflict(new { message = "تغير المبلغ المستحق. راجع السعر قبل التحصيل." });
        }
        if (request.PlanId.HasValue)
        {
            if (registrationPrice!.FinalAmount == 0 && request.ExceptionalDiscount is not null) {
                await ManualEnrollmentDiscount.RecordWaiver(db, t, actorId, enrollment, request.PlanId.Value, registrationPrice, key, payloadHash, clock, previousDiscount);
            } else {
            var paymentId = await collections.CreateCashAsync(t.AcademyId, actorId, enrollment.Id, request.PlanId.Value, key, request.PaymentMethod!, fixedSnapshot: registrationPrice, registrationPayloadHash: payloadHash);
            if (request.ExceptionalDiscount is not null) await ManualEnrollmentDiscount.Audit(db, t.AcademyId, actorId, paymentId, registrationPrice!, key, clock.UtcNow, previousDiscount);
            if (request.PaymentMethod == "Cash")
            {
                try { await cash.PostRegistration(request.CashSessionId!.Value, paymentId, key, payloadHash); }
                catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            }
        }
        }
        if (format != TrainingRegistrationFormat.Group)
        {
            try { await placement.FinishSpecialAdmission(group!, enrollment); }
            catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }
        await transaction.CommitAsync();
        return Results.Created($"/api/v1/people/players/{player.Id}", await RegistrationRead(db,t.AcademyId,actorId,request.PlanId.HasValue?key:null,player.Id,enrollment.Id,request.PlanId.HasValue,false));
    }

    // Return the identities of this exact committed registration, including on
    // replay. Clients verify these records instead of choosing the latest period
    // or claiming that an unverified POST response is a completed collection.
    private static async Task<object> RegistrationRead(FoundationDbContext db,Guid academy,Guid actor,string? key,Guid player,Guid? enrollment,bool subscription,bool replay)
    {
        var renewal=key==null?null:await db.RenewalRequests.AsNoTracking().Where(r=>r.AcademyId==academy&&r.RequestedByUserId==actor&&r.IdempotencyKey==key)
            .Select(r=>new{r.Id,r.SportEnrollmentId,r.SubscriptionPlanId,r.PaymentRequestId,r.RegistrationPayloadHash,r.OriginalAmount,r.DiscountAmount,r.FinalAmount,r.Currency}).SingleOrDefaultAsync();
        if(subscription&&renewal==null)throw new InvalidOperationException("Committed registration source missing");
        var period=renewal==null?null:await db.SubscriptionPeriods.AsNoTracking().Where(p=>p.AcademyId==academy&&p.SportEnrollmentId==renewal.SportEnrollmentId&&
            (p.CollectionId!=null&&db.Collections.Any(c=>c.Id==p.CollectionId&&c.RenewalRequestId==renewal.Id)||
            p.FreeTrainingApprovalId!=null&&db.FreeTrainingApprovals.Any(a=>a.Id==p.FreeTrainingApprovalId&&a.ActorUserId==actor&&a.IdempotencyKey==key&&a.RequestHash==renewal.RegistrationPayloadHash)))
            .Select(p=>new{p.Id,p.FreeTrainingApprovalId}).SingleOrDefaultAsync();
        Guid? receipt=renewal==null?null:await db.Receipts.Where(r=>r.AcademyId==academy&&r.Collection.RenewalRequestId==renewal.Id).Select(r=>(Guid?)r.Id).SingleOrDefaultAsync();
        static string Money(decimal value)=>value.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
        return new {playerId=player,playerCode=await db.Players.Where(p=>p.AcademyId==academy&&p.Id==player).Select(p=>p.PlayerCode).SingleAsync(),
            sportEnrollmentId=renewal?.SportEnrollmentId??enrollment,planId=renewal?.SubscriptionPlanId,periodId=period?.Id,paymentId=renewal?.PaymentRequestId,
            receiptId=receipt,waiverId=period?.FreeTrainingApprovalId,subscriptionCreated=subscription,replay,
            pricing=renewal==null?null:new{originalAmount=Money(renewal.OriginalAmount),discountAmount=Money(renewal.DiscountAmount),finalAmount=Money(renewal.FinalAmount),renewal.Currency}};
    }
}

public sealed record NamedRequest(string ArabicName, string? EnglishName, string? Description, SportKind? Kind = null);
public sealed record AgeCategoryRequest(string ArabicName, int? MinimumBirthYear, int? MaximumBirthYear);
public sealed record GroupRequest(string ArabicName, Guid BranchId, Guid SportId, Guid AgeCategoryId, int? Capacity);
public sealed record ScheduleRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record CoachAssignmentRequest(Guid MembershipId);
public sealed record RegistrationRequest(Guid? ExistingPlayerId, string? PlayerArabicName, DateOnly? DateOfBirth, Gender? Gender, Guid? ExistingGuardianId, string? GuardianName, string? GuardianPhone, string? RelationshipType, Guid BranchId, Guid SportId, Guid? GroupId, Guid? PlanId = null, string? PaymentMethod = null, Guid? DiscountRuleId = null,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal? ExpectedAmount = null, Guid? TrainingLevelId = null, bool LevelReviewed = false, Guid? CashSessionId = null, ExceptionalEnrollmentDiscount? ExceptionalDiscount = null, [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool AgeOverrideConfirmed = false);
