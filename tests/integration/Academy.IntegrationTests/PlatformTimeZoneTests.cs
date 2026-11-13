using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Importing;
using Academy.Api.Platform;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class PlatformSetupImportTests
{
    [Fact]
    public async Task Draft_with_historical_collection_cannot_change_the_paid_day()
    {
        using var platform = await Platform();
        var phone = "01096" + Random.Shared.Next(100000, 999999);
        var academy = await Create(platform, phone);
        var path = $"/api/v1/platform/academies/{academy}/setup";
        var initial = await platform.GetFromJsonAsync<JsonElement>(path);
        var input = TimeZoneSetup(initial.GetProperty("profile").GetProperty("editVersion").GetInt32(), phone);
        using var setup = await Send(platform, path, input, true);
        setup.EnsureSuccessStatusCode();
        input = input with { Version = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32() };
        await PublishCollectionWithoutSessions(platform, academy);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.TrainingSessions.IgnoreQueryFilters().AnyAsync(s => s.AcademyId == academy));
        var payment = await db.Collections.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.AcademyId == academy);
        Assert.Equal(600m, payment.Amount);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), payment.ConfirmedAtUtc);

        using var loaded = await platform.GetAsync(path);
        loaded.EnsureSuccessStatusCode();
        Assert.True(loaded.Headers.CacheControl?.NoStore);
        var before = await loaded.Content.ReadFromJsonAsync<JsonElement>();
        var reason = before.GetProperty("timeZoneLockReason").GetString();
        Assert.Contains("ترحيل تحصيلات سابقة", reason);
        Assert.Equal("Draft", before.GetProperty("profile").GetProperty("operationalState").GetString());
        using var denied = await Send(platform, path, input with { TimeZone = "Pacific/Honolulu", Location = "تغيير غير معتمد" }, true);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        var conflict = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("academy_time_zone_locked", conflict.GetProperty("code").GetString());
        Assert.Equal(reason, conflict.GetProperty("message").GetString());
        Assert.Equal("Africa/Cairo", conflict.GetProperty("timeZone").GetString());
        var unchanged = await platform.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("Africa/Cairo", unchanged.GetProperty("timeZone").GetString());
        Assert.Equal(input.Version, unchanged.GetProperty("profile").GetProperty("editVersion").GetInt32());
        Assert.Equal(input.Location, unchanged.GetProperty("profile").GetProperty("location").GetString());
        Assert.Equal(before.GetProperty("audit").GetArrayLength(), unchanged.GetProperty("audit").GetArrayLength());

        using var allowed = await Send(platform, path, input with { Location = "موقع محفوظ للاختبار" }, true);
        allowed.EnsureSuccessStatusCode();
        var after = await platform.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(input.Version + 1, after.GetProperty("profile").GetProperty("editVersion").GetInt32());
        Assert.Equal("موقع محفوظ للاختبار", after.GetProperty("profile").GetProperty("location").GetString());
        Assert.Equal("Africa/Cairo", after.GetProperty("timeZone").GetString());
        Assert.Equal(payment.ConfirmedAtUtc, await db.Collections.IgnoreQueryFilters().Where(x => x.AcademyId == academy).Select(x => x.ConfirmedAtUtc).SingleAsync());

        using var anonymous = factory.CreateClient();
        using var owner = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, path, input, true)).StatusCode);
        // An academy session and the platform's independent identity never share authorization.
        (await Send(owner, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" }, platform: false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(owner, path, input, true)).StatusCode);
    }

    [Fact]
    public async Task Draft_zone_change_before_collection_keeps_the_active_configuration_in_sync()
    {
        using var platform = await Platform();
        var phone = "01097" + Random.Shared.Next(100000, 999999);
        var academy = await Create(platform, phone);
        var sibling = await Create(platform, "01098" + Random.Shared.Next(100000, 999999));
        var basePath = $"/api/v1/platform/academies/{academy}";
        var path = basePath + "/setup";
        (await Send(platform, basePath + "/prepare-operation", new { })).EnsureSuccessStatusCode();
        var initial = await platform.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("timeZoneLockReason").ValueKind);
        var input = TimeZoneSetup(initial.GetProperty("profile").GetProperty("editVersion").GetInt32(), phone)
            with { Name = "أكاديمية اختبار التوقيت قبل النشر" };
        using var invalid = await Send(platform, path, input with { TimeZone = "Invalid/Zone" }, true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Contains("أدخل منطقة زمنية صحيحة", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());

        (await Send(platform, path, input with { TimeZone = " Pacific/Honolulu " }, true)).EnsureSuccessStatusCode();
        using var stale = await Send(platform, path, input, true);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("تغيرت البيانات", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        var saved = await platform.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("Pacific/Honolulu", saved.GetProperty("timeZone").GetString());
        Assert.Equal("Pacific/Honolulu", saved.GetProperty("configuration").GetProperty("timeZone").GetString());
        Assert.Equal(initial.GetProperty("configuration").GetProperty("features").GetRawText(), saved.GetProperty("configuration").GetProperty("features").GetRawText());
        Assert.Equal("Africa/Cairo", (await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/academies/{sibling}/setup")).GetProperty("timeZone").GetString());

        await PublishCollectionWithoutSessions(platform, academy);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var active = await db.AcademyConfigurationVersions.IgnoreQueryFilters().Where(x => x.AcademyId == academy && x.State == ConfigurationState.Active).SingleAsync();
        Assert.Equal("Pacific/Honolulu", AcademyRuntimeSettings.Read(active.Payload).TimeZone);
        Assert.Contains(await db.AcademyConfigurationVersions.IgnoreQueryFilters().Where(x => x.AcademyId == academy).Select(x => x.State).ToListAsync(), state => state == ConfigurationState.Superseded);
        var payment = await db.Collections.IgnoreQueryFilters().Where(x => x.AcademyId == academy).SingleAsync();
        Assert.Equal(600m, payment.Amount);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 22, 0, 0, TimeSpan.Zero), payment.ConfirmedAtUtc);
        Assert.Contains("تحصيلات سابقة", (await platform.GetFromJsonAsync<JsonElement>(path)).GetProperty("timeZoneLockReason").GetString());
    }

    private static PlatformSetup.SetupWrite TimeZoneSetup(int version, string phone) => new(version,
        "أكاديمية اختبار توقيت التحصيل", "موقع الاختبار", phone, phone, "Africa/Cairo", "Trial", 0,
        "EGP", "Monthly", new(2026, 9, 1), new(2027, 1, 1));

    private async Task PublishCollectionWithoutSessions(HttpClient platform, Guid academy)
    {
        await EnableCorrectionImport(platform, academy);
        var book = await MultisportSample(platform, academy);
        foreach (var sheet in new[] { "المواعيد الأسبوعية", "حصص فعلية اختيارية", "حضور اختياري" })
            book = WithRows(book, sheet, [book.Sheet(sheet).Rows[0]]);
        var bytes = ImportXlsx.Write(book);
        var preview = await Preview(platform, academy, bytes);
        Assert.True(preview.CanApprove, string.Join(" / ", preview.Errors.Select(x => x.Message)));
        Assert.Equal(1, preview.HistoricalPayments);
        Assert.Equal(0, preview.Historical!.Sessions);
        Assert.Equal(51, (await PublishCorrectionBatch(platform, academy, bytes, preview, historyApproved: true)).GetProperty("publishedRows").GetInt32());
    }
}
