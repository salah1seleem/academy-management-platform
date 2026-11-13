using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Importing;
using Academy.Api.Platform;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Import_calendar_worker_resolves_periods_on_the_academy_day_at_publication()
    {
        // Use a boundary beyond the dated roster changes in the shared demo database.
        var midnight = new DateTimeOffset(2036, 11, 11, 22, 0, 0, TimeSpan.Zero);
        var clock = factory.VisitClock;
        clock.Now = midnight.AddSeconds(-1);
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Demo:ReferenceDate", "");
            builder.UseSetting("Demo:SeedEnabled", "false");
        });
        using var owner = host.CreateClient();
        (await Write(owner, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" })).EnsureSuccessStatusCode();
        var fixture = await ImportFixture(owner, count: 5);
        var registration = fixture.Book.Sheet("Enrollments");
        var dates = new[]
        {
            ("2036-11-12", "2036-12-11", "5"),
            ("2036-10-13", "2036-11-11", "5"),
            ("2036-11-13", "2036-12-12", "5"),
            ("2036-10-15", "2036-11-13", "0"),
            ("2036-10-14", "2036-11-12", "2"),
        };
        var rows = registration.Rows.Select((row, index) => index == 0 ? row : EditPeriod(row, dates[index - 1])).ToArray();
        var book = new ImportWorkbook(fixture.Book.Sheets.Select(sheet => sheet.Name == registration.Name ? sheet with { Rows = rows } : sheet).ToArray());
        var brokenRows = rows.ToArray();
        brokenRows[1] = EditPeriod(rows[1], ("2036-11-12", "2036-11-11", "5"));
        brokenRows[2] = EditPeriod(rows[2], ("2036-10-13", "", "5"));
        var broken = new ImportWorkbook(book.Sheets.Select(sheet => sheet.Name == registration.Name ? sheet with { Rows = brokenRows } : sheet).ToArray());
        var rejected = (await (await PreviewFile(owner, ImportXlsx.Write(broken))).Content.ReadFromJsonAsync<ImportPreviewResult>())!;
        Assert.False(rejected.CanApprove);
        Assert.Contains(rejected.Errors, issue => issue.Sheet == "Enrollments" && issue.Row == 2 && issue.Column == "EndDate");
        Assert.Contains(rejected.Errors, issue => issue.Sheet == "Enrollments" && issue.Row == 3 && issue.Column == "EndDate");

        var preview = await ImportCheck(owner, book);
        Assert.Equal(new DateOnly(2036, 11, 11), preview.CutoffDate);
        var key = Guid.NewGuid().ToString("N");
        var id = await ImportId(await ImportStage(owner, book, preview, key));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.SubscriptionPeriods.AnyAsync(p => p.ImportJobId == id));
        clock.Now = midnight;
        Assert.Equal(id, await ImportWorker.Claim(host.Services.GetRequiredService<IServiceScopeFactory>(), clock));
        await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id);

        await using var read = host.Services.CreateAsyncScope();
        var verified = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var saved = await (from map in verified.ImportedEnrollments
                           join period in verified.SubscriptionPeriods on map.SubscriptionPeriodId equals period.Id
                           where map.JobId == id
                           orderby map.LegacyEnrollmentId
                           select new { period.Status, period.StartDate, period.EndDate, period.PriceSnapshot, period.SportEnrollmentId }).ToArrayAsync();
        Assert.Equal(new[] { SubscriptionPeriodStatus.Active, SubscriptionPeriodStatus.Expired, SubscriptionPeriodStatus.Scheduled, SubscriptionPeriodStatus.Expired, SubscriptionPeriodStatus.Active },
            saved.Select(row => row.Status).ToArray());
        Assert.Equal(new DateOnly(2036, 11, 12), saved[0].StartDate);
        Assert.Equal(new DateOnly(2036, 12, 11), saved[0].EndDate);
        Assert.Equal(new DateOnly(2036, 11, 11), await verified.AcademyImportJobs.Where(row => row.Id == id).Select(row => row.CutoffDate).SingleAsync());
        Assert.All(saved, row => Assert.Equal(540m, row.PriceSnapshot));
        var enrollments = saved.Select(row => row.SportEnrollmentId).ToArray();
        Assert.False(await verified.Collections.AnyAsync(row => enrollments.Contains(row.SportEnrollmentId)));
        Assert.False(await verified.Receipts.AnyAsync(row => enrollments.Contains(row.SportEnrollmentId)));
        Assert.Equal(id, await ImportId(await ImportStage(owner, book, preview, key)));
        Assert.Equal(5, await verified.SubscriptionPeriods.CountAsync(row => row.ImportJobId == id));
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/people/import/jobs/{id}/records")).StatusCode);

        IReadOnlyList<ImportCell> EditPeriod(IReadOnlyList<ImportCell> row, (string Start, string End, string Remaining) date) =>
            row.Select((cell, column) => registration.Rows[0][column].Value switch
            {
                "StartDate" => new ImportCell(date.Start, true),
                "EndDate" => new ImportCell(date.End, true),
                "RemainingSessions" => new ImportCell(date.Remaining, true),
                _ => cell,
            }).ToArray();
    }
}

public sealed partial class PlatformSetupImportTests
{
    [Fact]
    public async Task Import_calendar_templates_follow_the_selected_academy_even_without_a_member_context()
    {
        var clock = new VisitTestClock { Now = new DateTimeOffset(2026, 11, 11, 22, 0, 0, TimeSpan.Zero) };
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Demo:ReferenceDate", "");
            builder.UseSetting("Demo:SeedEnabled", "false");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(clock));
        });
        using var platform = host.CreateClient();
        (await Send(platform, "/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "246810" })).EnsureSuccessStatusCode();
        using var created = await Send(platform, "/api/v1/platform/academies", new PlatformAcademyWrite(
            Guid.NewGuid(), "calendar-" + Guid.NewGuid().ToString("N")[..12], "أكاديمية توقيت اصطناعية", "EGP", "Pacific/Honolulu",
            "مالك توقيت اصطناعي", $"{Guid.NewGuid():N}@example.test", "Demo-Only-123!", "multi-sport", "01096" + Random.Shared.Next(100000, 999999)));
        created.EnsureSuccessStatusCode();
        var otherAcademy = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var owner = host.CreateClient();
        (await Send(owner, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" }, platform: false)).EnsureSuccessStatusCode();
        const string ownerPath = "/api/v1/people/import/connected-template";
        var cairo = $"/api/v1/platform/academies/{DemoSeed.NogoomAcademyId}/import/connected-template";
        var honolulu = $"/api/v1/platform/academies/{otherAcademy}/import/connected-template";
        Assert.Equal("2026-11-12", await TemplateCutoff(owner, ownerPath));
        Assert.Equal("2026-11-12", await TemplateCutoff(platform, cairo));
        Assert.Equal("2026-11-11", await TemplateCutoff(platform, honolulu));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync(honolulu)).StatusCode);

        static async Task<string?> TemplateCutoff(HttpClient client, string path) =>
            ConnectedImportSchema.Meta(ImportXlsx.Read(await client.GetByteArrayAsync(path)), "تاريخ القطع");
    }
}
