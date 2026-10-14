using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class GuardianFeedbackEndpoints
{
    public static void MapGuardianFeedback(this WebApplication app)
    {
        var guardian = app.MapGroup("/api/v1/guardian/children/{playerId:guid}").RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        guardian.MapGet("/feedback-options", Options);
        guardian.MapPost("/feedback", Submit).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("guardian-feedback");
        var owner = app.MapGroup("/api/v1/manage/feedback").RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        owner.MapGet("/", List);
        owner.MapPost("/{id:guid}/review", Review).AddEndpointFilter<CsrfFilter>();
    }

    private static Guid Actor(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<bool> Linked(FoundationDbContext db, Guid academy, Guid actor, Guid player) =>
        await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == academy && x.PlayerId == player && x.IsActive && x.Guardian.IsActive && x.Guardian.UserId == actor && x.Player.IsActive);

    private static async Task<IResult> Options(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(principal);
        if (t.Role != AcademyRole.Guardian) return Results.Forbid();
        if (!await Linked(db, t.AcademyId, actor, playerId)) return Results.NotFound();
        var academyZone = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.TimeZone).SingleAsync();
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(academyZone));
        var periodMonth = new DateOnly(local.Year, local.Month, 1);
        var groupRows = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.IsActive && x.Status == EnrollmentStatus.Active && x.TrainingGroupId != null && x.TrainingGroup!.IsActive)
            .Select(x => new { id = x.TrainingGroupId!.Value, name = x.TrainingGroup!.ArabicName, sport = x.Sport.ArabicName }).Distinct().ToArrayAsync();
        var groupIds = groupRows.Select(x => x.id).ToArray();
        var assigned = await db.StaffGroupAssignments.Where(a => a.AcademyId == t.AcademyId && groupIds.Contains(a.TrainingGroupId) && a.IsActive && a.AcademyMembership.IsActive && a.AcademyMembership.Role == AcademyRole.Coach)
            .Select(a => new { a.TrainingGroupId, id = a.AcademyMembershipId, name = a.AcademyMembership.User.DisplayName }).ToArrayAsync();
        var groups = groupRows.Select(g => new { g.id, g.name, g.sport, coaches = assigned.Where(a => a.TrainingGroupId == g.id).Select(a => new { a.id, a.name }).Distinct().ToArray() }).ToArray();
        var mine = await db.GuardianFeedbacks.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.GuardianUserId == actor)
            .OrderByDescending(x => x.CreatedAtUtc).Take(30).Select(x => new { x.Id, x.Kind, x.CoachMembershipId, x.TrainingGroupId, x.Score, x.Message, x.Status, x.PeriodMonth, x.CreatedAtUtc }).ToArrayAsync();
        return Results.Ok(new { periodMonth, groups, mine });
    }

    private static async Task<IResult> Submit(Guid playerId, FeedbackWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(context.User);
        if (t.Role != AcademyRole.Guardian) return Results.Forbid();
        if (!await Linked(db, t.AcademyId, actor, playerId)) return Results.NotFound();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        var message = body.Message?.Trim() ?? "";
        if (key.Length is < 8 or > 100 || body.Kind is not ("Rating" or "Complaint" or "Suggestion") || message.Length > 500 ||
            body.Kind == "Rating" && (body.CoachMembershipId is null || body.Score is < 1 or > 5 || body.Score is null) ||
            body.Kind != "Rating" && (body.Score is not null || message.Length < 5))
            return Results.UnprocessableEntity(new { message = "اختر نوع الملاحظة ومجموعة الطفل، مع تقييم 1–5 أو نص واضح للشكوى والاقتراح." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback:{t.AcademyId}:{actor}:{key}"}, 0))");
        var previous = await db.GuardianFeedbacks.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.GuardianUserId == actor && x.IdempotencyKey == key);
        if (previous is not null)
            return previous.PlayerId == playerId && previous.Kind == body.Kind && previous.TrainingGroupId == body.TrainingGroupId && previous.CoachMembershipId == body.CoachMembershipId && previous.Score == body.Score && previous.Message == message
                ? Results.Ok(new { previous.Id, replay = true }) : Results.Conflict(new { message = "هذا المفتاح استُخدم لطلب مختلف." });
        var enrollment = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.IsActive && x.Status == EnrollmentStatus.Active && x.TrainingGroupId == body.TrainingGroupId && x.TrainingGroup!.IsActive)
            .Select(x => new { x.BranchId }).FirstOrDefaultAsync();
        if (enrollment is null) return Results.NotFound();
        if (body.CoachMembershipId.HasValue && !await db.StaffGroupAssignments.AnyAsync(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == body.TrainingGroupId && x.AcademyMembershipId == body.CoachMembershipId && x.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.Role == AcademyRole.Coach)) return Results.NotFound();
        var zone = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.TimeZone).SingleAsync();
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zone));
        var period = new DateOnly(local.Year, local.Month, 1);
        if (body.Kind == "Rating" && await db.GuardianFeedbacks.AnyAsync(x => x.AcademyId == t.AcademyId && x.Kind == "Rating" && x.GuardianUserId == actor && x.PlayerId == playerId && x.CoachMembershipId == body.CoachMembershipId && x.PeriodMonth == period))
            return Results.Conflict(new { message = "أرسلت تقييمًا لهذا المدرب وهذا الطفل خلال الشهر الحالي بالفعل." });
        var now = clock.GetUtcNow();
        var feedback = new GuardianFeedback { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = playerId, GuardianUserId = actor,
            BranchId = enrollment.BranchId, TrainingGroupId = body.TrainingGroupId, CoachMembershipId = body.CoachMembershipId,
            Kind = body.Kind, Score = body.Score, Message = message, PeriodMonth = period, Status = "Open", IdempotencyKey = key,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        db.GuardianFeedbacks.Add(feedback); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/api/v1/guardian/children/{playerId}/feedback-options", new { feedback.Id });
    }

    private static async Task<IResult> List(Guid? branchId, string? kind, string? status, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (kind is not (null or "Rating" or "Complaint" or "Suggestion") || status is not (null or "Open" or "Reviewed")) return Results.BadRequest(new { message = "فلتر غير صالح." });
        if (branchId.HasValue && !await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == branchId)) return Results.NotFound();
        return Results.Ok(await db.GuardianFeedbacks.Where(x => x.AcademyId == t.AcademyId && (!branchId.HasValue || x.BranchId == branchId) && (kind == null || x.Kind == kind) && (status == null || x.Status == status))
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(200)
            .Select(x => new { x.Id, x.Kind, x.Score, x.Message, x.Status, x.CreatedAtUtc, x.ReviewedAtUtc, x.PlayerId, playerName = db.Players.Where(p => p.AcademyId == t.AcademyId && p.Id == x.PlayerId).Select(p => p.ArabicName).FirstOrDefault(), x.BranchId, branchName = db.Branches.Where(b => b.AcademyId == t.AcademyId && b.Id == x.BranchId).Select(b => b.ArabicName).FirstOrDefault(), x.CoachMembershipId, coachName = db.AcademyMemberships.Where(c => c.AcademyId == t.AcademyId && c.Id == x.CoachMembershipId).Select(c => c.User.DisplayName).FirstOrDefault() }).ToArrayAsync());
    }

    private static async Task<IResult> Review(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var item = await db.GuardianFeedbacks.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (item is null) return Results.NotFound();
        if (item.Status == "Reviewed") return Results.Ok(new { item.Id, replay = true });
        item.Status = "Reviewed"; item.ReviewedByUserId = Actor(principal); item.ReviewedAtUtc = clock.GetUtcNow(); item.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(); return Results.Ok(new { item.Id });
    }
}

public sealed record FeedbackWrite(Guid TrainingGroupId, Guid? CoachMembershipId, string Kind, int? Score, string? Message);
