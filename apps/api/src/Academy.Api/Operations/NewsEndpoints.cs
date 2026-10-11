using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class NewsEndpoints
{
    public static void MapNews(this WebApplication app)
    {
        var staff = app.MapGroup("/api/v1/content/news").RequireAuthorization(AcademyPermissions.PlayerMediaManage);
        staff.MapGet("/", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.AcademyNews.Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, x.Title, x.Body, x.VisibleDays, x.IsActive, x.PublishedAtUtc, imageUrl = x.ImageAssetId == null ? null : "/api/v1/assets/" + x.ImageAssetId }).Take(100).ToListAsync()); });
        staff.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        staff.MapPut("/{id:guid}", Edit).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/news", async (CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => {
            var t = (await tenant.ResolveAsync())!; var now = clock.UtcNow;
            return Results.Ok(await db.AcademyNews.Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.PublishedAtUtc <= now && x.PublishedAtUtc.Value.AddDays(x.VisibleDays) > now).OrderByDescending(x => x.PublishedAtUtc).Select(x => new { x.Id, x.Title, x.Body, x.PublishedAtUtc, imageUrl = x.ImageAssetId == null ? null : "/api/v1/assets/" + x.ImageAssetId }).Take(10).ToListAsync());
        }).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static Task<IResult> Create(NewsWrite body, CurrentTenant t, FoundationDbContext db, ISubscriptionClock clock) => Save(null, body, t, db, clock);
    private static Task<IResult> Edit(Guid id, NewsWrite body, CurrentTenant t, FoundationDbContext db, ISubscriptionClock clock) => Save(id, body, t, db, clock);
    private static async Task<IResult> Save(Guid? id, NewsWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (string.IsNullOrWhiteSpace(body.Title) || body.Title.Length > 180 || string.IsNullOrWhiteSpace(body.Body) || body.Body.Length > 10000 || body.VisibleDays is < 1 or > 90) return Results.UnprocessableEntity(new { message = "أدخل عنوانًا وخبرًا ومدة ظهور بين يوم و90 يومًا." });
        if (body.ImageAssetId.HasValue && !await db.PrivateAssets.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.ImageAssetId && x.Purpose == "News" && x.ContentType.StartsWith("image/"))) return Results.NotFound();
        var row = id.HasValue ? await db.AcademyNews.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id) : null;
        if (id.HasValue && row is null) return Results.NotFound();
        if (row is null) { row = new AcademyNews { Id = Guid.NewGuid(), AcademyId = t.AcademyId, Title = body.Title.Trim(), Body = body.Body.Trim(), CreatedAtUtc = clock.UtcNow }; db.AcademyNews.Add(row); }
        row.Title = body.Title.Trim(); row.Body = body.Body.Trim(); row.ImageAssetId = body.ImageAssetId; row.VisibleDays = body.VisibleDays; row.IsActive = body.IsActive;
        if (!body.Publish) row.PublishedAtUtc = null; else row.PublishedAtUtc ??= clock.UtcNow;
        row.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); return Results.Ok(new { row.Id, row.PublishedAtUtc });
    }
}
public sealed record NewsWrite(string Title, string Body, Guid? ImageAssetId, int VisibleDays, bool Publish, bool IsActive);
