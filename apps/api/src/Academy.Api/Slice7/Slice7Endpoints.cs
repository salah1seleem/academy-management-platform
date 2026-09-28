using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice7;

public static class Slice7Endpoints
{
    public static void MapSlice7Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var guardian = api.MapGroup("/guardian").RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        guardian.MapGet("/catalog", GuardianCatalog);
        guardian.MapGet("/nutrition", GuardianNutrition);
        guardian.MapGet("/nutrition/{id:guid}", GuardianNutritionDetails);
        guardian.MapGet("/children/{playerId:guid}/medical", GuardianMedical);
        guardian.MapGet("/children/{playerId:guid}/media", GuardianMedia);

        var catalog = api.MapGroup("/content/catalog").RequireAuthorization(AcademyPermissions.SportCatalogManage);
        catalog.MapGet("/", CatalogList);
        catalog.MapGet("/{id:guid}", CatalogDetails);
        catalog.MapPost("/", CatalogCreate).AddEndpointFilter<CsrfFilter>();
        catalog.MapPut("/{id:guid}", CatalogUpdate).AddEndpointFilter<CsrfFilter>();
        catalog.MapPost("/{id:guid}/status", CatalogStatus).AddEndpointFilter<CsrfFilter>();

        var nutrition = api.MapGroup("/content/nutrition").RequireAuthorization(AcademyPermissions.NutritionManage);
        nutrition.MapGet("/", NutritionList);
        nutrition.MapGet("/{id:guid}", NutritionDetails);
        nutrition.MapPost("/", NutritionCreate).AddEndpointFilter<CsrfFilter>();
        nutrition.MapPut("/{id:guid}", NutritionUpdate).AddEndpointFilter<CsrfFilter>();
        nutrition.MapPost("/{id:guid}/status", NutritionStatus).AddEndpointFilter<CsrfFilter>();

        var medical = api.MapGroup("/medical-records").RequireAuthorization(AcademyPermissions.MedicalRecordManage);
        medical.MapGet("/", MedicalList);
        medical.MapGet("/{id:guid}", MedicalDetails);
        medical.MapPost("/", MedicalCreate).AddEndpointFilter<CsrfFilter>();
        medical.MapPut("/{id:guid}", MedicalUpdate).AddEndpointFilter<CsrfFilter>();
        medical.MapPost("/{id:guid}/publication", MedicalPublication).AddEndpointFilter<CsrfFilter>();

        var media = api.MapGroup("/player-media").RequireAuthorization(AcademyPermissions.PlayerMediaManage);
        media.MapGet("/", MediaList);
        media.MapGet("/{id:guid}", MediaDetails);
        media.MapPost("/", MediaCreate).AddEndpointFilter<CsrfFilter>();
        media.MapPut("/{id:guid}", MediaUpdate).AddEndpointFilter<CsrfFilter>();
        media.MapPost("/{id:guid}/publication", MediaPublication).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<IResult> GuardianCatalog(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        var linkedPlayers = db.GuardianPlayerLinks.Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.IsActive && x.Player.IsActive).Select(x => x.PlayerId);
        var sports = db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.Status == EnrollmentStatus.Active && linkedPlayers.Contains(x.PlayerId)).Select(x => x.SportId).Distinct();
        var rows = await db.SportCatalogItems.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive && sports.Contains(x.SportId)).OrderBy(x => x.Sport.ArabicName).ThenBy(x => x.DisplayOrder).ThenBy(x => x.ArabicName)
            .Select(x => new { x.Id, x.SportId, sport = x.Sport.ArabicName, x.ArabicName, x.ArabicDescription, x.ImageReference, x.DisplayPrice, x.Currency, x.DiscountPercentage }).ToListAsync();
        return Results.Ok(rows.GroupBy(x => new { x.SportId, x.sport }).Select(x => new { x.Key.SportId, sport = x.Key.sport, items = x.ToList() }));
    }

    private static async Task<IResult> GuardianNutrition(string? category, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var parsed = ParseCategory(category);
        var query = db.NutritionCategoryLinks.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.NutritionItem.IsActive);
        if (parsed.HasValue) query = query.Where(x => x.Category == parsed.Value);
        return Results.Ok(await query.OrderBy(x => x.Category).ThenBy(x => x.DisplayOrder).Select(x => new { x.NutritionItem.Id, x.NutritionItem.ArabicName, x.NutritionItem.ArabicDescription, x.NutritionItem.ImageReference, category = x.Category.ToString(), dataStatus = x.NutritionItem.DataStatus.ToString() }).ToListAsync());
    }

    private static async Task<IResult> GuardianNutritionDetails(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var row = await db.NutritionItems.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive).Select(x => new { x.Id, x.ArabicName, x.ArabicDescription, x.ImageReference, x.ServingDescription, x.Calories, x.ProteinGrams, x.CarbohydratesGrams, x.FatGrams, dataStatus = x.DataStatus.ToString(), x.SourceDescription, categories = x.Categories.OrderBy(c => c.Category).Select(c => c.Category.ToString()).ToList() }).SingleOrDefaultAsync();
        return row is null ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> GuardianMedical(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (!await Linked(db, t.AcademyId, UserId(principal), playerId)) return Results.NotFound();
        return Results.Ok(await db.PlayerMedicalRecords.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.IsActive && x.IsPublishedToGuardian).OrderByDescending(x => x.RecordDate)
            .Select(x => new { x.Id, type = x.RecordType.ToString(), x.ArabicTitle, x.RecordDate, x.ArabicDescription, status = x.Status.ToString(), x.GuardianVisibleNotes }).ToListAsync());
    }

    private static async Task<IResult> GuardianMedia(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (!await Linked(db, t.AcademyId, UserId(principal), playerId)) return Results.NotFound();
        return Results.Ok(await db.PlayerMedia.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.IsActive && x.IsPublishedToGuardian).OrderBy(x => x.DisplayOrder).ThenByDescending(x => x.CapturedAtUtc)
            .Select(x => new { x.Id, type = x.MediaType.ToString(), x.MediaReference, x.ThumbnailReference, x.ArabicCaption, x.CapturedAtUtc }).ToListAsync());
    }

    private static async Task<IResult> CatalogList(Guid? sportId, bool? active, string? search, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var q = db.SportCatalogItems.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (sportId.HasValue) q = q.Where(x => x.SportId == sportId); if (active.HasValue) q = q.Where(x => x.IsActive == active); if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.ArabicName.Contains(search.Trim()));
        return Results.Ok(await q.OrderBy(x => x.Sport.ArabicName).ThenBy(x => x.DisplayOrder).Select(x => new { x.Id, x.ArabicName, sport = x.Sport.ArabicName, x.SportId, x.ImageReference, x.DisplayPrice, x.Currency, x.DiscountPercentage, x.IsActive, x.DisplayOrder }).ToListAsync());
    }

    private static async Task<IResult> CatalogDetails(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.SportCatalogItems.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.SportId, x.ArabicName, x.EnglishName, x.ArabicDescription, x.ImageReference, x.DisplayPrice, x.Currency, x.DiscountPercentage, x.IsActive, x.DisplayOrder }).SingleOrDefaultAsync(); return row is null ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> CatalogCreate(CatalogWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var error = await ValidateCatalog(request, t.AcademyId, db); if (error is not null) return error;
        var row = new SportCatalogItem { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportId = request.SportId, ArabicName = request.ArabicName.Trim(), EnglishName = Clean(request.EnglishName), ArabicDescription = Clean(request.ArabicDescription), ImageReference = request.ImageReference, DisplayPrice = request.DisplayPrice, Currency = request.DisplayPrice.HasValue ? (Clean(request.Currency) ?? "EGP") : null, DiscountPercentage = request.DiscountPercentage, DisplayOrder = request.DisplayOrder, IsActive = request.IsActive, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.SportCatalogItems.Add(row); await db.SaveChangesAsync(); return Results.Created($"/api/v1/content/catalog/{row.Id}", new { row.Id });
    }

    private static async Task<IResult> CatalogUpdate(Guid id, CatalogWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.SportCatalogItems.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); var error = await ValidateCatalog(request, t.AcademyId, db); if (error is not null) return error;
        row.SportId = request.SportId; row.ArabicName = request.ArabicName.Trim(); row.EnglishName = Clean(request.EnglishName); row.ArabicDescription = Clean(request.ArabicDescription); row.ImageReference = request.ImageReference; row.DisplayPrice = request.DisplayPrice; row.Currency = request.DisplayPrice.HasValue ? (Clean(request.Currency) ?? "EGP") : null; row.DiscountPercentage = request.DiscountPercentage; row.DisplayOrder = request.DisplayOrder; row.IsActive = request.IsActive; row.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> CatalogStatus(Guid id, StatusWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) { var t = (await tenant.ResolveAsync())!; var row = await db.SportCatalogItems.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); row.IsActive = request.IsActive; row.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.Ok(new { row.IsActive }); }

    private static async Task<IResult> NutritionList(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.NutritionItems.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.ImageReference, dataStatus = x.DataStatus.ToString(), x.IsActive, categories = x.Categories.OrderBy(c => c.Category).Select(c => c.Category.ToString()).ToList() }).ToListAsync());
    }

    private static async Task<IResult> NutritionDetails(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.NutritionItems.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.ArabicName, x.ArabicDescription, x.ImageReference, x.ServingDescription, x.Calories, x.ProteinGrams, x.CarbohydratesGrams, x.FatGrams, x.DataStatus, x.SourceDescription, x.IsActive, categories = x.Categories.Select(c => c.Category).ToList() }).SingleOrDefaultAsync(); return row is null ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> NutritionCreate(NutritionWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var error = ValidateNutrition(request); if (error is not null) return error;
        var row = new NutritionItem { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = request.ArabicName.Trim(), ArabicDescription = request.ArabicDescription.Trim(), ImageReference = request.ImageReference, ServingDescription = request.ServingDescription.Trim(), Calories = request.Calories, ProteinGrams = request.ProteinGrams, CarbohydratesGrams = request.CarbohydratesGrams, FatGrams = request.FatGrams, DataStatus = request.DataStatus, SourceDescription = Clean(request.SourceDescription), IsActive = request.IsActive, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.NutritionItems.Add(row); foreach (var category in request.Categories.Distinct()) row.Categories.Add(new NutritionCategoryLink { AcademyId = t.AcademyId, NutritionItemId = row.Id, Category = category, DisplayOrder = request.DisplayOrder }); await db.SaveChangesAsync(); return Results.Created($"/api/v1/content/nutrition/{row.Id}", new { row.Id });
    }

    private static async Task<IResult> NutritionUpdate(Guid id, NutritionWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.NutritionItems.Include(x => x.Categories).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); var error = ValidateNutrition(request); if (error is not null) return error;
        row.ArabicName = request.ArabicName.Trim(); row.ArabicDescription = request.ArabicDescription.Trim(); row.ImageReference = request.ImageReference; row.ServingDescription = request.ServingDescription.Trim(); row.Calories = request.Calories; row.ProteinGrams = request.ProteinGrams; row.CarbohydratesGrams = request.CarbohydratesGrams; row.FatGrams = request.FatGrams; row.DataStatus = request.DataStatus; row.SourceDescription = Clean(request.SourceDescription); row.IsActive = request.IsActive; row.UpdatedAtUtc = clock.GetUtcNow(); db.NutritionCategoryLinks.RemoveRange(row.Categories); row.Categories = request.Categories.Distinct().Select(x => new NutritionCategoryLink { AcademyId = t.AcademyId, NutritionItemId = row.Id, Category = x, DisplayOrder = request.DisplayOrder }).ToList(); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> NutritionStatus(Guid id, StatusWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) { var t = (await tenant.ResolveAsync())!; var row = await db.NutritionItems.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); row.IsActive = request.IsActive; row.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.Ok(new { row.IsActive }); }

    private static async Task<IResult> MedicalList(Guid? playerId, MedicalRecordType? type, MedicalRecordStatus? status, bool? published, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var q = db.PlayerMedicalRecords.AsNoTracking().Where(x => x.AcademyId == t.AcademyId); if (playerId.HasValue) q = q.Where(x => x.PlayerId == playerId); if (type.HasValue) q = q.Where(x => x.RecordType == type); if (status.HasValue) q = q.Where(x => x.Status == status); if (published.HasValue) q = q.Where(x => x.IsPublishedToGuardian == published);
        return Results.Ok(await q.OrderByDescending(x => x.RecordDate).Select(x => new { x.Id, x.PlayerId, player = x.Player.ArabicName, type = x.RecordType.ToString(), x.ArabicTitle, x.RecordDate, status = x.Status.ToString(), x.IsPublishedToGuardian }).ToListAsync());
    }

    private static async Task<IResult> MedicalDetails(Guid id, CurrentTenant tenant, FoundationDbContext db) { var t = (await tenant.ResolveAsync())!; var row = await db.PlayerMedicalRecords.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.PlayerId, x.RecordType, x.ArabicTitle, x.RecordDate, x.ArabicDescription, x.Status, x.StaffNotes, x.GuardianVisibleNotes, x.IsPublishedToGuardian }).SingleOrDefaultAsync(); return row is null ? Results.NotFound() : Results.Ok(row); }

    private static async Task<IResult> MedicalCreate(MedicalWrite request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlayerId && x.IsActive)) return Results.NotFound(); if (string.IsNullOrWhiteSpace(request.ArabicTitle) || string.IsNullOrWhiteSpace(request.ArabicDescription)) return Validation("record", "العنوان والوصف مطلوبان."); var user = UserId(principal);
        var row = new PlayerMedicalRecord { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = request.PlayerId, RecordType = request.RecordType, ArabicTitle = request.ArabicTitle.Trim(), RecordDate = request.RecordDate, ArabicDescription = request.ArabicDescription.Trim(), Status = request.Status, StaffNotes = Clean(request.StaffNotes), GuardianVisibleNotes = Clean(request.GuardianVisibleNotes), IsPublishedToGuardian = request.IsPublishedToGuardian, CreatedByUserId = user, UpdatedByUserId = user, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }; db.PlayerMedicalRecords.Add(row); await db.SaveChangesAsync(); return Results.Created($"/api/v1/medical-records/{row.Id}", new { row.Id });
    }

    private static async Task<IResult> MedicalUpdate(Guid id, MedicalWrite request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.PlayerMedicalRecords.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlayerId && x.IsActive)) return Results.NotFound(); if (string.IsNullOrWhiteSpace(request.ArabicTitle) || string.IsNullOrWhiteSpace(request.ArabicDescription)) return Validation("record", "العنوان والوصف مطلوبان."); row.PlayerId = request.PlayerId; row.RecordType = request.RecordType; row.ArabicTitle = request.ArabicTitle.Trim(); row.RecordDate = request.RecordDate; row.ArabicDescription = request.ArabicDescription.Trim(); row.Status = request.Status; row.StaffNotes = Clean(request.StaffNotes); row.GuardianVisibleNotes = Clean(request.GuardianVisibleNotes); row.IsPublishedToGuardian = request.IsPublishedToGuardian; row.UpdatedByUserId = UserId(principal); row.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> MedicalPublication(Guid id, PublicationWrite request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock) { var t = (await tenant.ResolveAsync())!; var row = await db.PlayerMedicalRecords.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); row.IsPublishedToGuardian = request.IsPublished; row.UpdatedByUserId = UserId(principal); row.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.Ok(new { row.IsPublishedToGuardian }); }

    private static async Task<IResult> MediaList(Guid? playerId, PlayerMediaType? type, bool? published, CurrentTenant tenant, FoundationDbContext db) { var t = (await tenant.ResolveAsync())!; var q = db.PlayerMedia.AsNoTracking().Where(x => x.AcademyId == t.AcademyId); if (playerId.HasValue) q = q.Where(x => x.PlayerId == playerId); if (type.HasValue) q = q.Where(x => x.MediaType == type); if (published.HasValue) q = q.Where(x => x.IsPublishedToGuardian == published); return Results.Ok(await q.OrderBy(x => x.Player.ArabicName).ThenBy(x => x.DisplayOrder).Select(x => new { x.Id, x.PlayerId, player = x.Player.ArabicName, type = x.MediaType.ToString(), x.MediaReference, x.ThumbnailReference, x.ArabicCaption, x.IsPublishedToGuardian, x.DisplayOrder }).ToListAsync()); }

    private static async Task<IResult> MediaDetails(Guid id, CurrentTenant tenant, FoundationDbContext db) { var t = (await tenant.ResolveAsync())!; var row = await db.PlayerMedia.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.PlayerId, x.MediaType, x.MediaReference, x.ThumbnailReference, x.ArabicCaption, x.CapturedAtUtc, x.DisplayOrder, x.IsPublishedToGuardian }).SingleOrDefaultAsync(); return row is null ? Results.NotFound() : Results.Ok(row); }

    private static async Task<IResult> MediaCreate(MediaWrite request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlayerId && x.IsActive)) return Results.NotFound(); if (!SafeAsset(request.MediaReference) || request.ThumbnailReference is not null && !SafeAsset(request.ThumbnailReference)) return Validation("mediaReference", "مرجع الوسائط غير مسموح.");
        var row = new PlayerMedia { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = request.PlayerId, MediaType = request.MediaType, MediaReference = request.MediaReference, ThumbnailReference = Clean(request.ThumbnailReference), ArabicCaption = Clean(request.ArabicCaption), CapturedAtUtc = request.CapturedAtUtc, DisplayOrder = request.DisplayOrder, IsPublishedToGuardian = request.IsPublishedToGuardian, CreatedByUserId = UserId(principal), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }; db.PlayerMedia.Add(row); await db.SaveChangesAsync(); return Results.Created($"/api/v1/player-media/{row.Id}", new { row.Id });
    }

    private static async Task<IResult> MediaUpdate(Guid id, MediaWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.PlayerMedia.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PlayerId && x.IsActive)) return Results.NotFound(); if (!SafeAsset(request.MediaReference) || request.ThumbnailReference is not null && !SafeAsset(request.ThumbnailReference)) return Validation("mediaReference", "مرجع الوسائط غير مسموح."); row.PlayerId = request.PlayerId; row.MediaType = request.MediaType; row.MediaReference = request.MediaReference; row.ThumbnailReference = Clean(request.ThumbnailReference); row.ArabicCaption = Clean(request.ArabicCaption); row.CapturedAtUtc = request.CapturedAtUtc; row.DisplayOrder = request.DisplayOrder; row.IsPublishedToGuardian = request.IsPublishedToGuardian; row.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> MediaPublication(Guid id, PublicationWrite request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) { var t = (await tenant.ResolveAsync())!; var row = await db.PlayerMedia.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound(); row.IsPublishedToGuardian = request.IsPublished; row.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.Ok(new { row.IsPublishedToGuardian }); }

    private static async Task<IResult?> ValidateCatalog(CatalogWrite request, Guid academyId, FoundationDbContext db) { if (string.IsNullOrWhiteSpace(request.ArabicName) || !SafeAsset(request.ImageReference)) return Validation("catalog", "الاسم ومرجع صورة محلي مسموح مطلوبان."); if (request.DisplayPrice < 0 || request.DiscountPercentage is < 0 or > 100) return Validation("display", "السعر والخصم المعروضان غير صالحين."); if (!await db.Sports.AnyAsync(x => x.AcademyId == academyId && x.Id == request.SportId)) return Results.NotFound(); return null; }
    private static IResult? ValidateNutrition(NutritionWrite request) { if (string.IsNullOrWhiteSpace(request.ArabicName) || string.IsNullOrWhiteSpace(request.ArabicDescription) || string.IsNullOrWhiteSpace(request.ServingDescription) || !SafeAsset(request.ImageReference) || request.Categories.Count == 0) return Validation("nutrition", "الاسم والوصف والحصة والصورة والتصنيف مطلوبة."); if (request.Calories < 0 || request.ProteinGrams < 0 || request.CarbohydratesGrams < 0 || request.FatGrams < 0) return Validation("nutritionValues", "لا تقبل القيم الغذائية أرقامًا سالبة."); if (request.DataStatus == NutritionDataStatus.Reviewed && string.IsNullOrWhiteSpace(request.SourceDescription)) return Validation("source", "وصف المصدر مطلوب للبيانات المراجعة."); return null; }
    private static NutritionCategory? ParseCategory(string? value) => Enum.TryParse<NutritionCategory>(value, true, out var category) ? category : null;
    private static bool SafeAsset(string value) => value.StartsWith("/demo-assets/", StringComparison.Ordinal) && !value.Contains("..", StringComparison.Ordinal) && !value.Contains(':');
    private static async Task<bool> Linked(FoundationDbContext db, Guid academyId, Guid userId, Guid playerId) => await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == academyId && x.Guardian.UserId == userId && x.PlayerId == playerId && x.IsActive && x.Player.IsActive);
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed record CatalogWrite(Guid SportId, string ArabicName, string? EnglishName, string? ArabicDescription, string ImageReference, decimal? DisplayPrice, string? Currency, decimal? DiscountPercentage, int DisplayOrder, bool IsActive);
public sealed record NutritionWrite(string ArabicName, string ArabicDescription, string ImageReference, string ServingDescription, decimal? Calories, decimal? ProteinGrams, decimal? CarbohydratesGrams, decimal? FatGrams, NutritionDataStatus DataStatus, string? SourceDescription, IReadOnlyList<NutritionCategory> Categories, int DisplayOrder, bool IsActive);
public sealed record MedicalWrite(Guid PlayerId, MedicalRecordType RecordType, string ArabicTitle, DateOnly RecordDate, string ArabicDescription, MedicalRecordStatus Status, string? StaffNotes, string? GuardianVisibleNotes, bool IsPublishedToGuardian);
public sealed record MediaWrite(Guid PlayerId, PlayerMediaType MediaType, string MediaReference, string? ThumbnailReference, string? ArabicCaption, DateTimeOffset? CapturedAtUtc, int DisplayOrder, bool IsPublishedToGuardian);
public sealed record StatusWrite(bool IsActive);
public sealed record PublicationWrite(bool IsPublished);
