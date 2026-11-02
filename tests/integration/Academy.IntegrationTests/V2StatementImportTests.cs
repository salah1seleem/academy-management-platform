using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string ImportStatements = Statements + "/import";
    private static string CsvLine(StatementLineWrite b, string line = "1") => $"{line},{b.PaymentReference},{b.SettlementDate:yyyy-MM-dd},{b.GrossAmount.ToString("0.00", CultureInfo.InvariantCulture)},{b.FeeAmount.ToString("0.00", CultureInfo.InvariantCulture)},{b.NetAmount.ToString("0.00", CultureInfo.InvariantCulture)},{b.Currency},";
    private static StatementImportWrite Import(StatementLineWrite body, string? rows = null) => new(body.BranchId, body.SourceKind, body.AccountTag, body.StatementReference, body.Provider, StatementCsv.Header + "\n" + (rows ?? CsvLine(body)));
    private async Task<StatementImportWrite> ImportQuote(HttpClient c, StatementImportWrite body)
    {
        var r = await Post(c, ImportStatements + "/preview", body, await Csrf(c)); r.EnsureSuccessStatusCode();
        return body with { QuoteHash = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString() };
    }
    [Fact]
    public async Task Statement_import_previews_atomically_and_replays_concurrently_without_money_changes()
    {
        var source = await Statement(); var body = Import(source, CsvLine(source) + "\n" + CsvLine(source with { PaymentReference = "DEMO-UNMATCHED", GrossAmount = 110m, FeeAmount = 1m, NetAmount = 109m }, "2"));
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collections = await db.Collections.CountAsync(); var periods = await db.SubscriptionPeriods.CountAsync(); var receipts = await db.Receipts.CountAsync();
        body = await ImportQuote(owner, body); Assert.False(await db.SettlementComparisons.AnyAsync(x => x.StatementReference == source.StatementReference));
        var csrf = await Csrf(owner); var responses = await Task.WhenAll(Post(owner, ImportStatements, body, csrf), Post(owner, ImportStatements, body, csrf)); foreach (var r in responses) r.EnsureSuccessStatusCode();
        var first = await responses[0].Content.ReadFromJsonAsync<JsonElement>(); var second = await responses[1].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, first.GetProperty("count").GetInt32()); Assert.Equal(first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()), second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        Assert.Equal(2, await db.SettlementComparisons.CountAsync(x => x.StatementReference == source.StatementReference));
        Assert.Equal(collections, await db.Collections.CountAsync()); Assert.Equal(periods, await db.SubscriptionPeriods.CountAsync()); Assert.Equal(receipts, await db.Receipts.CountAsync());
        var replayPreview = await Post(owner, ImportStatements + "/preview", body, csrf); replayPreview.EnsureSuccessStatusCode(); Assert.Equal(2, (await replayPreview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reused").GetInt32());
    }
    [Fact]
    public async Task Statement_import_conflicting_existing_line_rolls_back_entire_new_batch()
    {
        var source = await Statement(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var single = await StatementQuote(owner, source); (await Post(owner, Statements, single, await Csrf(owner))).EnsureSuccessStatusCode();
        var body = Import(source, CsvLine(source with { GrossAmount = source.GrossAmount + 1, NetAmount = source.NetAmount + 1 }) + "\n" + CsvLine(source with { PaymentReference = "NEW-UNKNOWN" }, "2"));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ImportStatements + "/preview", body, await Csrf(owner))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ImportStatements, body with { QuoteHash = new string('A', 64) }, await Csrf(owner))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SettlementComparisons.CountAsync(x => x.StatementReference == source.StatementReference));
    }
    [Fact]
    public async Task Statement_import_stale_source_rejects_all_rows_and_allows_fresh_preview()
    {
        var source = await Statement(false); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var body = await ImportQuote(owner, Import(source, CsvLine(source) + "\n" + CsvLine(source with { PaymentReference = "OTHER" }, "2")));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var payment = await db.PaymentRequests.SingleAsync(x => x.ProviderReference == source.PaymentReference);
        using var callback = factory.CreateClient(); (await callback.PostAsJsonAsync(Events, await Signed(payment.Id))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ImportStatements, body, await Csrf(owner))).StatusCode);
        Assert.False(await db.SettlementComparisons.AnyAsync(x => x.StatementReference == source.StatementReference));
        body = await ImportQuote(owner, body); (await Post(owner, ImportStatements, body, await Csrf(owner))).EnsureSuccessStatusCode(); Assert.Equal(2, await db.SettlementComparisons.CountAsync(x => x.StatementReference == source.StatementReference));
    }
    [Fact]
    public async Task Statement_import_never_matches_the_same_collection_twice_or_bypasses_single_line_guard()
    {
        var source = await Statement(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var csrf = await Csrf(owner);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ImportStatements + "/preview", Import(source, CsvLine(source) + "\n" + CsvLine(source, "2")), csrf)).StatusCode);
        var single = await StatementQuote(owner, source); (await Post(owner, Statements, single, csrf)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ImportStatements + "/preview", Import(source with { StatementReference = "ANOTHER-" + Guid.NewGuid() }), csrf)).StatusCode);
    }
    [Theory] [InlineData("invalid-row")] [InlineData("future-date")] [InlineData("zero-amount")]
    public async Task Invalid_import_is_atomic_and_does_not_ignore_a_bad_row(string kind)
    {
        var source = await Statement(); using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        var invalid = kind switch { "future-date" => CsvLine(source with { SettlementDate = new(2099, 1, 1) }, "2"), "zero-amount" => CsvLine(source with { GrossAmount = 0, FeeAmount = 0, NetAmount = 0 }, "2"), _ => "bad,row" };
        var body = Import(source, CsvLine(source) + "\n" + invalid); var r = await Post(owner, ImportStatements + "/preview", body, await Csrf(owner)); Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, ImportStatements, body with { QuoteHash = new string('A', 64) }, await Csrf(owner))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SettlementComparisons.AnyAsync(x => x.StatementReference == source.StatementReference));
    }
    [Fact]
    public async Task Statement_import_owner_only_csrf_and_foreign_references_do_not_disclose_collections()
    {
        var source = await Statement(); var body = Import(source); using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(ImportStatements + "/preview", body)).StatusCode);
        var template = await owner.GetAsync(ImportStatements + "/template"); template.EnsureSuccessStatusCode(); Assert.Contains(StatementCsv.Header, await template.Content.ReadAsStringAsync());
        using var other = await InboxStaff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await Post(other, ImportStatements + "/preview", body, await Csrf(other))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var branch = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Branches.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync();
        var foreign = await Post(other, ImportStatements + "/preview", body with { BranchId = branch }, await Csrf(other)); foreign.EnsureSuccessStatusCode(); var row = (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0]; Assert.Equal("Unmatched", row.GetProperty("result").GetString()); Assert.Equal(JsonValueKind.Null, row.GetProperty("expected").ValueKind);
        foreach (var email in new[] { DemoSeed.AdminEmail, DemoSeed.CoachEmail }) { using var staff = await InboxStaff(email); Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, ImportStatements + "/preview", body, await Csrf(staff))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, ImportStatements, body, await Csrf(staff))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ImportStatements + "/template")).StatusCode); }
        using var guardian = await GuardianClient(); Assert.Equal(HttpStatusCode.Forbidden, (await Post(guardian, ImportStatements, body, await Csrf(guardian))).StatusCode);
    }
    [Fact]
    public async Task Statement_import_250_rows_retains_all_discrepancies_once_and_ignores_csv_formatting_on_retry()
    {
        var source = await Statement(false); var csv = string.Join('\n', Enumerable.Range(1, 250).Select(i => CsvLine(source with { PaymentReference = "DEMO-MISSING-" + i }, i.ToString(CultureInfo.InvariantCulture))));
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); var body = await ImportQuote(owner, Import(source, csv));
        var first = await Post(owner, ImportStatements, body, await Csrf(owner)); first.EnsureSuccessStatusCode(); Assert.Equal(250, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        var retry = await Post(owner, ImportStatements, body with { CsvText = "\uFEFF" + body.CsvText.Replace("\n", "\r\n") + "\r\n" }, await Csrf(owner)); retry.EnsureSuccessStatusCode(); Assert.Equal(250, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reused").GetInt32());
    }
    [Fact]
    public async Task Statement_import_can_extend_a_statement_without_replacing_its_saved_line()
    {
        var source = await Statement(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var single = await StatementQuote(owner, source);
        var saved = await Post(owner, Statements, single, await Csrf(owner)); saved.EnsureSuccessStatusCode(); var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var body = await ImportQuote(owner, Import(source, CsvLine(source) + "\n" + CsvLine(source with { PaymentReference = "DEMO-NEW-ROW" }, "2")));
        var response = await Post(owner, ImportStatements, body, await Csrf(owner)); response.EnsureSuccessStatusCode(); var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32()); Assert.Equal(1, result.GetProperty("reused").GetInt32()); Assert.Equal(id, result.GetProperty("items")[0].GetProperty("id").GetGuid());
    }
    [Fact]
    public async Task Statement_import_is_rate_limited_without_storing_previews()
    {
        var source = await Statement(false); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var csrf = await Csrf(owner); var body = Import(source);
        for (var i = 0; i < 6; i++) (await Post(owner, ImportStatements + "/preview", body, csrf)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(owner, ImportStatements + "/preview", body, csrf)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SettlementComparisons.AnyAsync(x => x.StatementReference == source.StatementReference));
    }
}
