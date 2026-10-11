using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static class GroupPlacementEndpoints
{
    public static void MapGroupPlacement(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithMetadata(new AcademyFeatureMetadata("training"));
        var manage = api.MapGroup("/manage").RequireAuthorization(AcademyPermissions.StructureManage);
        manage.MapGet("/training-levels", async (FoundationDbContext db) => Results.Ok(await db.TrainingLevels.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Rank).ThenBy(x => x.ArabicName).Select(x => new { x.Id, x.SportId, x.ArabicName, x.Rank }).ToArrayAsync()));
        manage.MapPost("/training-levels", async (TrainingLevelWrite b, FoundationDbContext db, GroupPlacementService service, TimeProvider time) => await Run(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var t = await service.Authority("academy", "Add");
            if (string.IsNullOrWhiteSpace(b.ArabicName) || b.ArabicName.Trim().Length > 100 || b.Rank is < 0 or > 1000 || !await db.Sports.AnyAsync(x => x.Id == b.SportId && x.IsActive)) throw new FacilityRejected("أدخل اسم المستوى ورياضته وترتيبه بشكل صحيح.", 422);
            if (await db.TrainingLevels.AnyAsync(x => x.SportId == b.SportId && x.ArabicName == b.ArabicName.Trim())) throw new FacilityRejected("المستوى موجود بالفعل لهذه الرياضة.");
            var row = new TrainingLevel { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportId = b.SportId, ArabicName = b.ArabicName.Trim(), Rank = b.Rank, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() }; db.TrainingLevels.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id };
        })).AddEndpointFilter<CsrfFilter>();
        manage.MapPut("/groups/{id:guid}/placement-level", async (Guid id, PlacementLevelWrite b, FoundationDbContext db, GroupPlacementService service, TimeProvider time) => await Run(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var t = await service.Authority("academy", "Edit");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{id}"}, 0))");
            var group = await db.TrainingGroups.FromSqlInterpolated($"SELECT *, xmin FROM \"TrainingGroups\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync() ?? throw new FacilityRejected("المجموعة غير متاحة.", 404); await service.Authority("academy", "Edit", group.BranchId);
            if (b.LevelId.HasValue && !await db.TrainingLevels.AnyAsync(x => x.Id == b.LevelId && x.SportId == group.SportId && x.IsActive)) throw new FacilityRejected("المستوى لا يطابق رياضة المجموعة.", 422);
            if (await db.SportEnrollments.AnyAsync(x => x.IsActive && (x.TrainingGroupId == id || db.EnrollmentGroupTransfers.Any(tr => tr.SportEnrollmentId == x.Id && tr.TargetGroupId == id)) && (x.TrainingLevelId != b.LevelId || b.LevelId != null && (x.LevelReviewedAtUtc == null || x.LevelReviewedByUserId == null)))) throw new FacilityRejected("راجع مستويات أعضاء المجموعة والنقل المجدول أولًا؛ لا يغير الإعداد أهلية اللاعبين تلقائيًا.");
            group.TrainingLevelId = b.LevelId; group.UpdatedAtUtc = time.GetUtcNow(); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { group.Id, group.TrainingLevelId };
        })).AddEndpointFilter<CsrfFilter>();
        var players = api.MapGroup("/people").RequireAuthorization(AcademyPermissions.PeopleManage);
        players.MapGet("/placement-levels", async (FoundationDbContext db) => Results.Ok(await db.TrainingLevels.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Rank).Select(x => new { x.Id, x.SportId, x.ArabicName }).ToArrayAsync()));
        players.MapPut("/enrollments/{id:guid}/placement-level", async (Guid id, PlacementLevelWrite b, FoundationDbContext db, GroupPlacementService service, TimeProvider time) => await Run(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var t = await service.Authority("players", "Edit");
            var row = await db.SportEnrollments.FromSqlInterpolated($"SELECT *, xmin FROM \"SportEnrollments\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync() ?? throw new FacilityRejected("التسجيل غير متاح.", 404); await service.Authority("players", "Edit", row.BranchId);
            if (b.LevelId.HasValue && !await db.TrainingLevels.AnyAsync(x => x.Id == b.LevelId && x.SportId == row.SportId && x.IsActive)) throw new FacilityRejected("المستوى لا يطابق رياضة التسجيل.", 422);
            if (row.TrainingGroupId.HasValue)
            {
                if (await db.TrainingGroups.AnyAsync(g => (g.Id == row.TrainingGroupId || db.EnrollmentGroupTransfers.Any(tr => tr.SportEnrollmentId == id && tr.TargetGroupId == g.Id)) && g.TrainingLevelId.HasValue && g.TrainingLevelId != b.LevelId)) throw new FacilityRejected("المستوى الجديد لا يطابق المجموعة الحالية أو النقل المجدول؛ استخدم مسار النقل والترقية المراجع.");
            }
            row.TrainingLevelId = b.LevelId; row.LevelReviewedByUserId = b.LevelId.HasValue ? db.RequestUserId : null; row.LevelReviewedAtUtc = b.LevelId.HasValue ? time.GetUtcNow() : null; row.UpdatedAtUtc = time.GetUtcNow(); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, row.TrainingLevelId, row.LevelReviewedAtUtc };
        })).AddEndpointFilter<CsrfFilter>();
        manage.MapGet("/groups/{id:guid}/placement-candidates", async (Guid id, string? search, int? offset, Guid? enrollmentId, GroupPlacementService service) => await Run(() => service.Candidates(id, search, offset ?? 0, enrollmentId))).RequireAuthorization(AcademyPermissions.PeopleManage);
        manage.MapPut("/groups/{id:guid}/training-format", async (Guid id, GroupTrainingFormatWrite body, GroupPlacementService service) => await Run(() => service.TrainingFormat(id, body))).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>();
        players.MapGet("/enrollments/{id:guid}/placement-preferences", async (Guid id, GroupPlacementService service) => await Run(() => service.PreferenceOptions(id))).RequireAuthorization(AcademyPermissions.StructureManage);
        players.MapPost("/enrollments/{id:guid}/placement-recommendations", async (Guid id, PlacementPreferences body, GroupPlacementService service) => await Run(() => service.Recommend(id, body))).RequireAuthorization(AcademyPermissions.StructureManage).AddEndpointFilter<CsrfFilter>();
        players.MapPost("/enrollments/{id:guid}/group-transfer/preview", async (Guid id, GroupTransferWrite body, GroupPlacementService service) => await Run(() => service.PreviewTransfer(id, body))).RequireAuthorization(AcademyPermissions.StructureManage).AddEndpointFilter<CsrfFilter>();
        players.MapPost("/enrollments/{id:guid}/group-transfer", async (Guid id, GroupTransferWrite body, GroupPlacementService service, HttpRequest request) => await Run(() => service.Transfer(id, body, request.Headers["Idempotency-Key"].ToString()))).RequireAuthorization(AcademyPermissions.StructureManage).AddEndpointFilter<CsrfFilter>();
        players.MapGet("/enrollments/{id:guid}/group-transfers", async (Guid id, FoundationDbContext db) => !await db.SportEnrollments.AnyAsync(x => x.Id == id) ? Results.NotFound() : Results.Ok(await db.EnrollmentGroupTransfers.Where(x => x.SportEnrollmentId == id).OrderBy(x => x.EffectiveDate).Select(x => new { x.Id, x.SourceGroupId, x.TargetGroupId, x.EffectiveDate, x.CreatedAtUtc }).ToArrayAsync()));
        manage.MapPost("/groups/{id:guid}/placement/preview", async (Guid id, PlacementWrite body, GroupPlacementService service) => await Run(() => service.Preview(id, body))).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/groups/{id:guid}/placement", async (Guid id, PlacementWrite body, GroupPlacementService service, HttpRequest request) => await Run(() => service.Save(id, body, request.Headers["Idempotency-Key"].ToString()))).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> action) { try { return Results.Ok(await action()); } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); } }
}
