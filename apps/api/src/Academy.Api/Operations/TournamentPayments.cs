using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class TournamentPayments
{
    private static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapTournamentPayments(this WebApplication app)
    {
        var guardian = app.MapGroup("/api/v1/guardian/tournament-payments").RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        guardian.MapGet("/options", Options);
        guardian.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        guardian.MapGet("/{id:guid}", GuardianDetails);
        guardian.MapPost("/{id:guid}/proof", Proof).AddEndpointFilter<CsrfFilter>();
        var manage = app.MapGroup("/api/v1/manage/tournament-payments");
        manage.MapGet("/{id:guid}", StaffDetails).RequireAuthorization(AcademyPermissions.PaymentRead);
        manage.MapPost("/{id:guid}/review", Review).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Options(CurrentTenant tenant, ClaimsPrincipal user, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(user);
        var enrollments = await db.SportEnrollments.Where(e => e.AcademyId == t.AcademyId && e.IsActive && e.Player.IsActive && e.Sport.IsActive && e.Branch.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == e.PlayerId && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == actor)).Select(e => new { e.Id, e.SportId, player = e.Player.ArabicName, branch = e.Branch.ArabicName }).ToListAsync();
        var sports = enrollments.Select(e => e.SportId).Distinct().ToArray();
        return Results.Ok(new { enrollments, tournaments = await db.AcademyTournaments.Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.Fee > 0 && sports.Contains(x.SportId)).Select(x => new { x.Id, x.Name, x.SportId, x.Fee, x.Currency }).ToListAsync(), payments = await db.TournamentPayments.Where(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == actor).OrderByDescending(x => x.CreatedAtUtc).Take(50).Select(x => new { x.Id, x.TournamentName, x.Status, x.Amount, x.Currency }).ToListAsync() });
    }
    private static async Task<IResult> Create(TournamentPaymentWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(context.User); var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return Results.UnprocessableEntity();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"tournament-key:{t.AcademyId}:{actor}:{key}"}, 0))");
        var replay = await db.TournamentPayments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == actor && x.IdempotencyKey == key);
        if (replay != null) return replay.EnrollmentId == body.EnrollmentId && replay.TournamentId == body.TournamentId ? Results.Ok(new { replay.Id, replay = true }) : Results.Conflict();
        var enrollment = await db.SportEnrollments.SingleOrDefaultAsync(e => e.AcademyId == t.AcademyId && e.Id == body.EnrollmentId && e.IsActive && e.Player.IsActive && e.Sport.IsActive && e.Branch.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == e.PlayerId && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == actor));
        if (enrollment == null) return Results.NotFound();
        var tournament = await db.AcademyTournaments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.TournamentId && x.IsActive && x.Fee > 0 && x.SportId == enrollment.SportId);
        if (tournament == null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"tournament-child:{t.AcademyId}:{body.TournamentId}:{body.EnrollmentId}"}, 0))");
        var existing = await db.TournamentPayments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.TournamentId == body.TournamentId && x.EnrollmentId == body.EnrollmentId);
        if (existing != null) return Results.Conflict(new { message = "يوجد طلب رسم لهذه البطولة بالفعل. راجعه من طلباتك؛ يمكنك إعادة رفع الإثبات إذا رُفض." });
        var row = new TournamentPayment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EnrollmentId = body.EnrollmentId, TournamentId = body.TournamentId, RequestedByUserId = actor, TournamentName = tournament.Name, Amount = tournament.Fee, Currency = tournament.Currency, IdempotencyKey = key, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.TournamentPayments.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/guardian/tournament-payments/{row.Id}", new { row.Id });
    }
    private static Task<IResult> GuardianDetails(Guid id, CurrentTenant t, ClaimsPrincipal user, FoundationDbContext db) => Details(id, t, db, Actor(user));
    private static Task<IResult> StaffDetails(Guid id, CurrentTenant t, FoundationDbContext db) => Details(id, t, db, null);
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db, Guid? actor)
    {
        var t = (await tenant.ResolveAsync())!;
        var row = await db.TournamentPayments.Where(x => x.AcademyId == t.AcademyId && x.Id == id && (!actor.HasValue || x.RequestedByUserId == actor && db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.Id == x.EnrollmentId && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == e.PlayerId && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == actor)))).Select(x => new { x.Id, x.TournamentName, x.Amount, x.Currency, x.Status, x.ReceiptNumber, x.ConfirmedAtUtc, x.ReviewReason, proofUrl = x.ProofAssetId == null ? null : "/api/v1/assets/" + x.ProofAssetId, player = db.SportEnrollments.Where(e => e.AcademyId == t.AcademyId && e.Id == x.EnrollmentId).Select(e => e.Player.ArabicName).Single() }).SingleOrDefaultAsync();
        return row == null ? Results.NotFound() : Results.Ok(row);
    }
    private static async Task<IResult> Proof(Guid id, ProofWrite body, CurrentTenant tenant, ClaimsPrincipal user, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(user); await using var tx = await db.Database.BeginTransactionAsync(); await Lock(t.AcademyId, id, db);
        var row = await db.TournamentPayments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.RequestedByUserId == actor);
        if (row == null || !await db.PrivateAssets.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.AssetId && x.Purpose == "Proof" && x.UploadedByUserId == actor)) return Results.NotFound();
        if (!await db.SportEnrollments.AnyAsync(e => e.AcademyId == t.AcademyId && e.Id == row.EnrollmentId && e.IsActive && e.Player.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == e.PlayerId && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == actor))) return Results.NotFound();
        if (row.Status == "Confirmed") return Results.Conflict();
        row.ProofAssetId = body.AssetId; row.Status = "Pending"; row.ReviewReason = null; row.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { message = "وصل إثبات رسم البطولة للإدارة. لا يغير اشتراك التدريب." });
    }
    private static Task Lock(Guid academy, Guid id, FoundationDbContext db) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"tournament-review:{academy}:{id}"}, 0))");
    private static async Task<IResult> Review(Guid id, TournamentReviewWrite body, CurrentTenant tenant, ClaimsPrincipal user, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; if (body.Reason?.Length > 500) return Results.UnprocessableEntity();
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(t.AcademyId, id, db);
        var row = await db.TournamentPayments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row == null) return Results.NotFound();
        var wanted = body.Accept ? "Confirmed" : "Rejected";
        if (row.Status != "Pending") return row.Status == wanted ? Results.Ok(new { row.Id, row.ReceiptNumber, replay = true }) : Results.Conflict();
        if (body.Accept && (!row.ProofAssetId.HasValue || !await db.SportEnrollments.AnyAsync(e => e.AcademyId == t.AcademyId && e.Id == row.EnrollmentId && e.IsActive && e.Player.IsActive))) return Results.Conflict(new { message = "يلزم إثبات دفع وتسجيل لاعب قائم لقبول رسم البطولة." });
        row.Status = wanted; row.ReviewReason = body.Reason?.Trim(); row.UpdatedAtUtc = clock.UtcNow;
        if (body.Accept) { row.ConfirmedByUserId = Actor(user); row.ConfirmedAtUtc = clock.UtcNow; row.PaymentMethod = "Online"; row.ReceiptNumber = $"TRN-{clock.Today:yyyyMMdd}-{row.Id.ToString("N")[..12].ToUpperInvariant()}"; }
        db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = row.RequestedByUserId, AudienceRole="Guardian",TargetKind="Tournament",TargetId=row.Id, Message = body.Accept ? $"تم قبول رسم {row.TournamentName}. الإيصال متاح في طلب البطولة؛ اشتراك التدريب لم يتغير." : $"رُفض إثبات رسم {row.TournamentName}. {body.Reason?.Trim() ?? "راجع الإدارة."}", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id, row.ReceiptNumber });
    }
}
public sealed record TournamentPaymentWrite(Guid TournamentId, Guid EnrollmentId);
public sealed record TournamentReviewWrite(bool Accept, string? Reason);
