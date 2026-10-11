using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/assets").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapPost("/upload", Upload).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/{id:guid}", Download);
        api.MapGet("/gallery", Gallery);
    }
    private static string Root(IConfiguration config, IWebHostEnvironment env) => Path.GetFullPath(config["Media:PrivateRoot"] ?? Path.Combine(env.ContentRootPath, "local-uploads"));
    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Task<bool> Linked(FoundationDbContext db, Guid academy, Guid user, Guid player) => db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == academy && x.Guardian.UserId == user && x.PlayerId == player && x.IsActive && x.Guardian.IsActive && x.Player.IsActive);
    private static Task<bool> Assigned(FoundationDbContext db, Guid academy, Guid user, Guid group) => db.StaffGroupAssignments.AnyAsync(x => x.AcademyId == academy && x.AcademyMembership.UserId == user && x.TrainingGroupId == group && x.IsActive && x.AcademyMembership.IsActive && x.TrainingGroup.IsActive);

    private static async Task<IResult> Upload(HttpContext context, CurrentTenant tenant, FoundationDbContext db, IAuthorizationService authorization, IConfiguration config, IWebHostEnvironment env, ISubscriptionClock clock)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new { message = "اختر ملف صورة أو فيديو." });
        IFormCollection form;
        try { form = await context.Request.ReadFormAsync(); }
        catch (InvalidDataException) { return Results.BadRequest(new { message = "حجم الملف أكبر من المسموح." }); }
        var file = form.Files.GetFile("file"); var purpose = form["purpose"].ToString();
        var t = (await tenant.ResolveAsync())!; var actor = Actor(context.User);
        Guid? playerId = Guid.TryParse(form["playerId"], out var player) ? player : null;
        Guid? groupId = Guid.TryParse(form["groupId"], out var group) ? group : null;
        if (file is null || file.Length < 12 || file.Length > 50 * 1024 * 1024 || form.Files.Count != 1 || form["caption"].ToString().Length > 300)
            return Results.BadRequest(new { message = "ارفع ملفًا واحدًا لا يتجاوز 50 ميجابايت." });
        if (purpose is not ("Photo" or "Media" or "Proof" or "News" or "StaffPhoto")) return Results.BadRequest();
        if (purpose == "Proof")
        {
            if (t.Role != AcademyRole.Guardian || playerId.HasValue || groupId.HasValue) return Results.Forbid();
        }
        else
        {
            var permission = purpose == "StaffPhoto" ? AcademyPermissions.PayrollManage : purpose == "Photo" ? AcademyPermissions.PeopleManage : AcademyPermissions.PlayerMediaManage;
            var staffAllowed = (await authorization.AuthorizeAsync(context.User, permission)).Succeeded;
            if (!staffAllowed && !(purpose == "Media" && t.Role == AcademyRole.Coach && groupId.HasValue && !playerId.HasValue && await Assigned(db, t.AcademyId, actor, groupId.Value))) return Results.Forbid();
            if (purpose == "News" && (playerId.HasValue || groupId.HasValue)) return Results.BadRequest();
            if (purpose == "StaffPhoto" && (playerId.HasValue || groupId.HasValue)) return Results.BadRequest();
            if (purpose == "Photo" && (!playerId.HasValue || groupId.HasValue)) return Results.BadRequest();
            if (purpose == "Media" && playerId.HasValue == groupId.HasValue) return Results.BadRequest(new { message = "اختر لاعبًا أو مجموعة واحدة." });
            if (playerId.HasValue && !await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId)) return Results.NotFound();
            if (groupId.HasValue && !await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId && x.IsActive)) return Results.NotFound();
        }
        await using var source = file.OpenReadStream();
        var header = new byte[12]; await source.ReadExactlyAsync(header);
        var detected = DetectType(header);
        if (detected is null || detected != file.ContentType || ((purpose is "Photo" or "Proof" or "News" or "StaffPhoto") && !detected.StartsWith("image/")))
            return Results.BadRequest(new { message = "الأنواع المدعومة: JPEG وPNG وWebP، وMP4 وMOV." });
        if (detected.StartsWith("image/") && file.Length > 8 * 1024 * 1024) return Results.BadRequest(new { message = "الصورة لا تتجاوز 8 ميجابايت." });
        var now = clock.UtcNow;
        var asset = new PrivateAsset { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UploadedByUserId = actor, ContentType = detected, StorageName = Guid.NewGuid().ToString("N"), Length = file.Length, Purpose = purpose, PlayerId = playerId, TrainingGroupId = groupId, Caption = form["caption"].ToString().Trim(), CreatedAtUtc = now, UpdatedAtUtc = now };
        var root = Root(config, env); Directory.CreateDirectory(root); var path = Path.Combine(root, asset.StorageName);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await output.WriteAsync(header); await source.CopyToAsync(output); }
        db.PrivateAssets.Add(asset);
        if (purpose == "Media" && groupId.HasValue)
        {
            var roster = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == groupId && x.IsActive && x.Player.IsActive).Select(x => x.PlayerId).Distinct().ToListAsync();
            db.PrivateAssetAudiences.AddRange(roster.Select(id => new PrivateAssetAudience { Id = Guid.NewGuid(), AcademyId = t.AcademyId, AssetId = asset.Id, PlayerId = id, CreatedAtUtc = now, UpdatedAtUtc = now }));
        }
        if (purpose == "Photo")
        {
            var row = await db.Players.SingleAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId);
            row.PhotoReference = $"/api/v1/assets/{asset.Id}"; row.UpdatedAtUtc = now;
        }
        try { await db.SaveChangesAsync(); }
        catch { File.Delete(path); throw; }
        return Results.Created($"/api/v1/assets/{asset.Id}", new { asset.Id, url = $"/api/v1/assets/{asset.Id}", asset.ContentType });
    }

    internal static string? DetectType(byte[] b)
    {
        if (b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff) return "image/jpeg";
        if (b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (System.Text.Encoding.ASCII.GetString(b, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(b, 8, 4) == "WEBP") return "image/webp";
        if (System.Text.Encoding.ASCII.GetString(b, 4, 4) == "ftyp") return System.Text.Encoding.ASCII.GetString(b, 8, 4) == "qt  " ? "video/quicktime" : "video/mp4";
        return null;
    }

    private static async Task<bool> CanRead(PrivateAsset asset, TenantMembership t, ClaimsPrincipal principal, FoundationDbContext db, IAuthorizationService authorization, ISubscriptionClock clock)
    {
        var user = Actor(principal); var policy = asset.Purpose switch { "StaffPhoto" => AcademyPermissions.PayrollRead, "Photo" => AcademyPermissions.PeopleManage, "Proof" => AcademyPermissions.PaymentRead, _ => AcademyPermissions.PlayerMediaRead };
        if ((t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) && (await authorization.AuthorizeAsync(principal, policy)).Succeeded) return true;
        if (!asset.IsActive) return false;
        if (asset.Purpose == "StaffPhoto") return false;
        if (asset.Purpose == "Proof") return asset.UploadedByUserId == user;
        var now = clock.UtcNow;
        if (asset.Purpose == "News") return await db.AcademyNews.AnyAsync(x => x.AcademyId == t.AcademyId && x.ImageAssetId == asset.Id && x.IsActive && x.PublishedAtUtc <= now && x.PublishedAtUtc.Value.AddDays(x.VisibleDays) > now);
        if (asset.PlayerId.HasValue && await Linked(db, t.AcademyId, user, asset.PlayerId.Value)) return true;
        if (asset.Purpose == "Media" && asset.TrainingGroupId.HasValue)
            return await Assigned(db, t.AcademyId, user, asset.TrainingGroupId.Value) || await db.PrivateAssetAudiences.AnyAsync(x => x.AcademyId == t.AcademyId && x.AssetId == asset.Id && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.PlayerId && l.Guardian.UserId == user && l.IsActive && l.Guardian.IsActive && l.Player.IsActive));
        return false;
    }
    private static async Task<IResult> Download(Guid id, HttpContext context, CurrentTenant tenant, FoundationDbContext db, IAuthorizationService authorization, IConfiguration config, IWebHostEnvironment env, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        var asset = await db.PrivateAssets.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (asset is null || !await CanRead(asset, t, context.User, db, authorization, clock)) return Results.NotFound();
        var path = Path.Combine(Root(config, env), asset.StorageName); if (!File.Exists(path)) return Results.NotFound();
        context.Response.Headers.CacheControl = "private, no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(path, asset.ContentType, enableRangeProcessing: true);
    }
    private static async Task<IResult> Gallery(Guid? playerId, Guid? groupId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IAuthorizationService authorization)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal);
        var query = db.PrivateAssets.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Purpose == "Media" && x.IsActive);
        if (t.Role == AcademyRole.Guardian)
        {
            if (!playerId.HasValue || !await Linked(db, t.AcademyId, user, playerId.Value)) return Results.NotFound();
            query = query.Where(x => x.PlayerId == playerId || db.PrivateAssetAudiences.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == playerId && e.AssetId == x.Id));
        }
        else if (t.Role == AcademyRole.Coach)
        {
            if (!groupId.HasValue || !await Assigned(db, t.AcademyId, user, groupId.Value)) return Results.NotFound();
            query = query.Where(x => x.TrainingGroupId == groupId);
        }
        else
        {
            if (!(await authorization.AuthorizeAsync(principal, AcademyPermissions.PlayerMediaRead)).Succeeded) return Results.Forbid();
            if (playerId.HasValue) query = query.Where(x => x.PlayerId == playerId || db.PrivateAssetAudiences.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == playerId && e.AssetId == x.Id)); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        }
        return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new { x.Id, x.PlayerId, x.TrainingGroupId, x.ContentType, x.Caption, x.CreatedAtUtc, url = "/api/v1/assets/" + x.Id }).ToListAsync());
    }
}
