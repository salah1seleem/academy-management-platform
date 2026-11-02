using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;
public static partial class SkillCurriculumEndpoints
{
    private const string Root = "/api/v1/evaluations/skills";
    private static readonly string[] Units = ["Checklist", "Count", "Seconds", "Metres"];
    public static void MapSkillCurriculumEndpoints(this WebApplication app)
    {
        var g = app.MapGroup(Root).RequireAuthorization(AcademyPermissions.EvaluationRead);
        g.MapGet("/options", Options); g.MapGet("/curricula", Catalogue); g.MapGet("/curricula/{id:guid}", CurriculumDetail);
        g.MapPost("/curricula", CreateCurriculum).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        g.MapPut("/curricula/{id:guid}", EditCurriculum).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/curricula/{id:guid}/publish", PublishCurriculum).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/curricula/{id:guid}/retire", RetireCurriculum).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        MapSkillJourneys(app);
    }
    private static IResult Bad(string message) => Results.BadRequest(new { message });
    private static IResult Changed(string message = "تغيرت نسخة الخطة؛ أعد تحميلها قبل الحفظ.") => Results.Conflict(new { message });
    private static string Hash(object value, string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key + JsonSerializer.Serialize(value))));
    private static bool AllBranches(TenantMembership t, FoundationDbContext db) => t.Role == AcademyRole.AcademyOwner || t.Role == AcademyRole.AcademyAdmin && !db.BranchScopeRestricted;
    private static async Task<bool> CanDefine(TenantMembership t, FoundationDbContext db, HttpContext h, bool add) => AllBranches(t, db) && await PlayerGoalEndpoints.CanWrite(t, db, h, add, AcademyPermissions.EvaluationCriteriaManage);
    private static IQueryable<SkillCurriculum> Readable(TenantMembership t, FoundationDbContext db)
    {
        var q = db.SkillCurricula.AsNoTracking().Where(c => c.AcademyId == t.AcademyId);
        if (!AllBranches(t, db)) q = q.Where(c => c.PublishedAtUtc != null);
        if (t.Role == AcademyRole.Coach) q = q.Where(c => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.AcademyMembershipId == t.MembershipId && a.IsActive && a.TrainingGroup.IsActive && a.TrainingGroup.SportId == c.SportId && a.TrainingGroup.Branch.IsActive && a.TrainingGroup.Sport.IsActive));
        return q;
    }
    private static object CurriculumRow(SkillCurriculum c) => new { c.Id, c.TrainingLevelId, c.SportId, c.RevisionNumber, c.EditRevision, c.Title, c.LevelNameSnapshot, c.State, c.CopiedFromId, c.Version, c.PublishedAtUtc, c.RetiredAtUtc };
    private static object RequirementRow(SkillRequirement r) => new { r.Id, r.Name, r.Unit, r.Protocol, r.DisplayOrder };
    private static async Task LockLevel(FoundationDbContext db, Guid academy, Guid level) => await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"skill-curriculum:{academy}:{level}"},0))");
    private static bool Valid(CurriculumWrite r) => r.TrainingLevelId != Guid.Empty && !string.IsNullOrWhiteSpace(r.Title) && r.Title.Trim().Length <= 100 && r.Requirements is { Count: >= 1 and <= 20 }
        && r.Requirements.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Name) && x.Name.Trim().Length <= 120 && !string.IsNullOrWhiteSpace(x.Protocol) && x.Protocol.Trim().Length <= 500 && Units.Contains(x.Unit))
        && r.Requirements.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == r.Requirements.Count;
    private static void Requirements(FoundationDbContext db, SkillCurriculum c, IReadOnlyList<SkillRequirementWrite> inputs, DateTimeOffset now)
    {
        db.SkillRequirements.AddRange(inputs.Select((x, i) => new SkillRequirement { Id = Guid.NewGuid(), AcademyId = c.AcademyId, CurriculumId = c.Id, SportId = c.SportId, Name = x.Name.Trim(), Protocol = x.Protocol.Trim(), Unit = x.Unit, DisplayOrder = i, CreatedAtUtc = now, UpdatedAtUtc = now }));
    }
    private static async Task<IResult> Options(CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        var levels = db.TrainingLevels.Where(l => l.AcademyId == t.AcademyId && l.IsActive && db.Sports.Any(s => s.Id == l.SportId && s.IsActive));
        if (t.Role == AcademyRole.Coach) levels = levels.Where(l => db.StaffGroupAssignments.Any(a => a.AcademyMembershipId == t.MembershipId && a.IsActive && a.TrainingGroup.IsActive && a.TrainingGroup.SportId == l.SportId));
        return Results.Ok(new { levels = await levels.OrderBy(l => l.SportId).ThenBy(l => l.Rank).Select(l => new { l.Id, l.SportId, l.ArabicName, sport = db.Sports.Where(s => s.Id == l.SportId).Select(s => s.ArabicName).First() }).ToArrayAsync(),
            sports = t.Role == AcademyRole.AcademyOwner ? await db.Sports.Where(s => s.IsActive).Select(s => new { s.Id, name = s.ArabicName }).ToArrayAsync() : [], canCreateLevel = t.Role == AcademyRole.AcademyOwner, canCreate = await CanDefine(t, db, h, true), canEdit = await CanDefine(t, db, h, false) });
    }
    private static async Task<IResult> Catalogue(Guid? levelId, CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        var q = Readable(t, db); if (levelId.HasValue) q = q.Where(c => c.TrainingLevelId == levelId);
        var rows = await q.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id).Take(101).ToArrayAsync();
        return Results.Ok(new { items = rows.Take(100).Select(CurriculumRow), hasMore = rows.Length > 100, canCreate = await CanDefine(t, db, h, true) });
    }
    private static async Task<IResult> CurriculumDetail(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; var c = await Readable(t, db).SingleOrDefaultAsync(c => c.Id == id); if (c == null) return Results.NotFound();
        var skills = await db.SkillRequirements.AsNoTracking().Where(r => r.AcademyId == t.AcademyId && r.CurriculumId == id).OrderBy(r => r.DisplayOrder).ToArrayAsync();
        return Results.Ok(new { item = CurriculumRow(c), requirements = skills.Select(RequirementRow), equalWeights = true, canEdit = await CanDefine(t, db, h, false), canCopy = await CanDefine(t, db, h, true) });
    }
    private static async Task<IResult> CreateCurriculum(CurriculumWrite request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (request.RequestId == Guid.Empty || !Valid(request)) return Bad("أدخل اسم النسخة ومستوى موجودًا ومن1إلى20 مهارة مختلفة مع وحدة وبروتوكول لكل منها.");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!AllBranches(t, db) || !await PlayerGoalEndpoints.LockAuthority(t, db, h, null, request.RequestId, true, AcademyPermissions.EvaluationCriteriaManage)) return Results.Forbid();
        var level = await db.TrainingLevels.SingleOrDefaultAsync(l => l.AcademyId == t.AcademyId && l.Id == request.TrainingLevelId && l.IsActive && db.Sports.Any(s => s.Id == l.SportId && s.IsActive)); if (level == null) return Results.NotFound();
        await LockLevel(db, t.AcademyId, level.Id); var hash = Hash(request, "curriculum-create");
        var replay = await db.SkillCurricula.SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.CreatedByUserId == db.RequestUserId && c.CreationRequestId == request.RequestId);
        if (replay != null) return replay.CreationHash == hash ? Results.Ok(new { replay.Id, replay = true }) : Changed();
        if (request.CopiedFromId.HasValue) { var source = await Readable(t, db).SingleOrDefaultAsync(c => c.Id == request.CopiedFromId); if (source == null) return Results.NotFound(); if (source.TrainingLevelId != level.Id || source.Version != request.SourceVersion) return Changed(); }
        var next = (await db.SkillCurricula.Where(c => c.AcademyId == t.AcademyId && c.TrainingLevelId == level.Id).Select(c => (int?)c.RevisionNumber).MaxAsync() ?? 0) + 1; var now = clock.GetUtcNow();
        var row = new SkillCurriculum { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingLevelId = level.Id, SportId = level.SportId, RevisionNumber = next, Title = request.Title.Trim(), LevelNameSnapshot = level.ArabicName,
            CopiedFromId = request.CopiedFromId, CreationRequestId = request.RequestId, CreationHash = hash, CreatedByUserId = db.RequestUserId, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.SkillCurricula.Add(row); Requirements(db, row, request.Requirements, now);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Changed(); }
        return Results.Created($"{Root}/curricula/{row.Id}", new { row.Id });
    }
    private static async Task<IResult> EditCurriculum(Guid id, CurriculumWrite request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (request.RequestId == Guid.Empty || !Valid(request)) return Bad("راجع اسم النسخة ومتطلبات المهارات ووحداتها وبروتوكولاتها.");
        var source = await Readable(t, db).SingleOrDefaultAsync(c => c.Id == id); if (source == null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); if (!AllBranches(t, db) || !await PlayerGoalEndpoints.LockAuthority(t, db, h, null, request.RequestId, false, AcademyPermissions.EvaluationCriteriaManage)) return Results.Forbid();
        await LockLevel(db, t.AcademyId, source.TrainingLevelId); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SkillCurricula\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        var c = await db.SkillCurricula.SingleAsync(c => c.Id == id && c.AcademyId == t.AcademyId); if (c.State != "Draft" || c.Version != request.Version || c.TrainingLevelId != request.TrainingLevelId) return Changed("لا يعدل المنشور أو مستوى النسخة. افتح أحدث مسودة أو انسخ المنشور.");
        // No player can reference draft requirements. Replace only this locked draft;
        // their identities become stable on publication, never rewrite a learner's history.
        await db.SkillRequirements.Where(r => r.AcademyId == t.AcademyId && r.CurriculumId == id).ExecuteDeleteAsync();
        var now = clock.GetUtcNow(); c.Title = request.Title.Trim(); c.EditRevision++; c.UpdatedAtUtc = now; Requirements(db, c, request.Requirements, now);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { c.Id, c.Version });
    }
    private static async Task<IResult> PublishCurriculum(Guid id, CurriculumAction request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (request.RequestId == Guid.Empty) return Bad("معرف طلب النشر مطلوب."); var source = await Readable(t, db).SingleOrDefaultAsync(c => c.Id == id); if (source == null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); if (!AllBranches(t, db) || !await PlayerGoalEndpoints.LockAuthority(t, db, h, null, request.RequestId, false, AcademyPermissions.EvaluationCriteriaManage)) return Results.Forbid();
        await LockLevel(db, t.AcademyId, source.TrainingLevelId); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SkillCurricula\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        var c = await db.SkillCurricula.SingleAsync(c => c.Id == id && c.AcademyId == t.AcademyId);
        if (c.State == "Published" && c.PublishRequestId == request.RequestId && c.PublishedByUserId == db.RequestUserId) return Results.Ok(new { c.Id, replay = true });
        if (c.State != "Draft" || c.Version != request.Version || await db.SkillCurricula.AnyAsync(x => x.TrainingLevelId == c.TrainingLevelId && x.State == "Published" && x.RevisionNumber > c.RevisionNumber)) return Changed();
        var level = await db.TrainingLevels.SingleOrDefaultAsync(l => l.Id == c.TrainingLevelId && l.IsActive && db.Sports.Any(s => s.Id == l.SportId && s.IsActive)); if (level == null) return Results.NotFound();
        var count = await db.SkillRequirements.CountAsync(r => r.CurriculumId == id); if (count is < 1 or > 20) return Bad("تحتاج النسخة من1إلى20 متطلبًا قبل النشر.");
        c.LevelNameSnapshot = level.ArabicName; c.State = "Published"; c.PublishedAtUtc = clock.GetUtcNow(); c.PublishedByUserId = db.RequestUserId; c.PublishRequestId = request.RequestId; c.EditRevision++; c.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { c.Id, c.Version });
    }
    private static async Task<IResult> RetireCurriculum(Guid id, CurriculumAction request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (request.RequestId == Guid.Empty) return Bad("معرف طلب الإيقاف مطلوب."); var source = await Readable(t, db).SingleOrDefaultAsync(c => c.Id == id); if (source == null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); if (!AllBranches(t, db) || !await PlayerGoalEndpoints.LockAuthority(t, db, h, null, request.RequestId, false, AcademyPermissions.EvaluationCriteriaManage)) return Results.Forbid();
        await LockLevel(db, t.AcademyId, source.TrainingLevelId); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SkillCurricula\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        var c = await db.SkillCurricula.SingleAsync(c => c.Id == id && c.AcademyId == t.AcademyId); if (c.State == "Retired") return Results.Ok(new { c.Id, unchanged = true }); if (c.Version != request.Version) return Changed();
        c.State = "Retired"; c.RetiredByUserId = db.RequestUserId; c.RetiredAtUtc = clock.GetUtcNow(); c.EditRevision++; c.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { c.Id });
    }
}
public sealed record SkillRequirementWrite(string Name, string Unit, string Protocol);
public sealed record CurriculumWrite(Guid TrainingLevelId, string Title, IReadOnlyList<SkillRequirementWrite> Requirements, Guid RequestId, uint Version = 0, Guid? CopiedFromId = null, uint SourceVersion = 0);
public sealed record CurriculumAction(uint Version, Guid RequestId);
