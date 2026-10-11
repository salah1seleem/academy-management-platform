using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Platform;

public static partial class PlatformAcademies
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,79}$")] private static partial Regex Slug();
    public static void MapPlatformAcademies(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/platform").RequireAuthorization(PlatformIdentity.Policy);
        api.MapGet("/audit", async (FoundationDbContext db) => Results.Ok(await db.PlatformAudits.AsNoTracking().OrderByDescending(x => x.AtUtc).Take(100).Select(x => new { x.Id, x.Action, x.TargetAcademyId, x.AtUtc, x.Details }).ToListAsync()));
        api.MapGet("/academies/{id:guid}/entitlements", async (Guid id, FoundationDbContext db) => {
            if (!await db.Academies.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.AcademyFeatureEntitlements.AsNoTracking().Where(x => x.AcademyId == id && x.IsActive).OrderBy(x => x.Feature).Select(x => new { x.Feature, x.Enabled, x.PlanCode }).ToListAsync());
        });
        api.MapPut("/academies/{id:guid}/entitlements", Licenses).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/academies", Provision).AddEndpointFilter<CsrfFilter>();
    }
    private static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<IResult> Licenses(Guid id, PlatformLicenseWrite body, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        if (body.Features is null || body.Features.Count != AcademyFeatures.All.Length || AcademyFeatures.All.Any(f => !body.Features.ContainsKey(f)) || body.PlanCode is not ("Starter" or "Premium" or "Trial" or "LegacyDemo"))
            return Results.Problem(statusCode: 422, title: "اختر خطة وقائمة ميزات كاملة وصحيحة");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + id}, 0))");
        if (!await db.Academies.AnyAsync(x => x.Id == id)) return Results.NotFound();
        var now = clock.GetUtcNow(); var rows = await db.AcademyFeatureEntitlements.Where(x => x.AcademyId == id).ToListAsync();
        var before = rows.ToDictionary(x => x.Feature, x => x.Enabled && x.IsActive);
        foreach (var f in AcademyFeatures.All)
        {
            var row = rows.SingleOrDefault(x => x.Feature == f);
            if (row is null) { row = new() { Id = Guid.NewGuid(), AcademyId = id, Feature = f, PlanCode = body.PlanCode, CreatedAtUtc = now, UpdatedAtUtc = now }; db.AcademyFeatureEntitlements.Add(row); }
            row.Enabled = body.Features[f]; row.PlanCode = body.PlanCode; row.IsActive = true; row.UpdatedAtUtc = now;
        }
        db.PlatformAudits.Add(new() { Id = Guid.NewGuid(), ActorUserId = Actor(actor), TargetAcademyId = id, Action = "academy.entitlements", Details = JsonSerializer.Serialize(new { before, after = body.Features, body.PlanCode }), AtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
    }
    private static async Task<IResult> Provision(PlatformAcademyWrite body, FoundationDbContext db, ClaimsPrincipal actor, UserManager<ApplicationUser> users, AcademyConfigurationService configs, TimeProvider clock)
    {
        if (body.RequestId == Guid.Empty || !Slug().IsMatch(body.Slug ?? "") || string.IsNullOrWhiteSpace(body.ArabicName) || body.ArabicName.Length > 200 || body.Currency is not ("EGP" or "USD" or "SAR" or "AED") || string.IsNullOrWhiteSpace(body.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(body.TimeZone, out _) || string.IsNullOrWhiteSpace(body.OwnerName) || body.OwnerName.Length > 160 || string.IsNullOrWhiteSpace(body.OwnerEmail) || body.OwnerEmail.Length > 256 || !System.Net.Mail.MailAddress.TryCreate(body.OwnerEmail, out _) || string.IsNullOrWhiteSpace(body.InitialPassword) || body.InitialPassword.Length > 128 || body.Template is not ("football" or "swimming" or "multi-sport"))
            return Results.Problem(statusCode: 422, title: "راجع بيانات الأكاديمية وحساب المالك");
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { body.Slug, body.ArabicName, body.Currency, body.TimeZone, body.OwnerEmail, body.OwnerName, body.Template }));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"provision:" + body.RequestId}, 0))");
        var repeated = await db.PlatformAudits.SingleOrDefaultAsync(x => x.Id == body.RequestId);
        if (repeated is not null)
        {
            using var details = JsonDocument.Parse(repeated.Details);
            return repeated.ActorUserId == Actor(actor) && repeated.Action == "academy.provision" && details.RootElement.TryGetProperty("hash", out var savedHash) && savedHash.GetString() == hash
                ? Results.Ok(new { id = repeated.TargetAcademyId, status = "Draft", repeated = true }) : Results.Conflict(new { message = "معرّف الطلب مستخدم لعملية أخرى." });
        }
        if (await db.Academies.AnyAsync(x => x.Slug == body.Slug) || await users.FindByEmailAsync(body.OwnerEmail) is not null) return Results.Conflict(new { message = "اسم رابط الأكاديمية أو بريد المالك مستخدم. لا يُربط حساب قائم تلقائيًا." });
        var now = clock.GetUtcNow();
        var academy = new Academy.Infrastructure.Tenancy.Academy { Id = Guid.NewGuid(), Slug = body.Slug!, ArabicName = body.ArabicName.Trim(), DefaultCurrency = body.Currency, TimeZone = body.TimeZone, IsActive = false, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Academies.Add(academy);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = body.OwnerEmail.Trim(), Email = body.OwnerEmail.Trim(), DisplayName = body.OwnerName.Trim(), IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var created = await users.CreateAsync(user, body.InitialPassword);
        if (!created.Succeeded) return Results.Problem(statusCode: 422, title: "تعذر إنشاء حساب المالك؛ استخدم كلمة مرور قوية وبريدًا غير مستخدم");
        db.AcademyMemberships.Add(new() { Id = Guid.NewGuid(), AcademyId = academy.Id, UserId = user.Id, Role = AcademyRole.AcademyOwner, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.AcademyExperiences.Add(new() { Id = Guid.NewGuid(), AcademyId = academy.Id, DisplayName = academy.ArabicName, CoachAttendanceEnabled = false, CoachAttendancePolicyConfirmed = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var f in AcademyFeatures.All) db.AcademyFeatureEntitlements.Add(new() { Id = Guid.NewGuid(), AcademyId = academy.Id, Feature = f, Enabled = false, PlanCode = "Trial", CreatedAtUtc = now, UpdatedAtUtc = now });
        db.PlatformAudits.Add(new() { Id = body.RequestId, ActorUserId = Actor(actor), TargetAcademyId = academy.Id, Action = "academy.provision", Details = JsonSerializer.Serialize(new { hash, body.Template }), AtUtc = now });
        await db.SaveChangesAsync();
        await configs.CreateDraft(academy.Id, Actor(actor), AcademyRuntimeSettings.Initial(body.Currency, body.TimeZone));
        await tx.CommitAsync(); return Results.Ok(new { id = academy.Id, status = "Draft", repeated = false });
    }
}
public sealed record PlatformLicenseWrite(string PlanCode, Dictionary<string, bool> Features);
public sealed record PlatformAcademyWrite(Guid RequestId, string? Slug, string ArabicName, string Currency, string TimeZone, string OwnerName, string OwnerEmail, string InitialPassword, string Template);
