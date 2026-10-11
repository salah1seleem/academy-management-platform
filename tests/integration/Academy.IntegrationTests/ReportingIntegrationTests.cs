using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Api.Slice8;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class ReportingIntegrationTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private Factory factory = null!;
    public async Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); await Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact] public async Task Owner_dashboard_total_equals_collections_sum() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/owner-summary?from=2026-01-01&to=2026-12-31"); Assert.Equal(await NetRevenue(), j.GetProperty("collections").GetProperty("total").GetDecimal()); }
    [Fact] public async Task Failed_payment_is_excluded_from_revenue() { await AssertPaymentStatusNotRevenue(PaymentRequestStatus.Failed); }
    [Fact] public async Task Pending_payment_is_excluded_from_revenue() { await AssertPaymentStatusNotRevenue(PaymentRequestStatus.Pending); }
    [Fact] public async Task Cancelled_payment_is_excluded_from_revenue() { await AssertPaymentStatusNotRevenue(PaymentRequestStatus.Cancelled); }
    [Fact] public async Task Financial_report_filters_by_date() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/financial?from=2026-09-27&to=2026-09-28"); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.StartsWith("2026-09-", x.GetProperty("confirmedAtUtc").GetString())); }
    [Fact] public async Task Financial_report_filters_by_sport() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, $"/api/v1/reports/financial?from=2026-01-01&to=2026-12-31&sportId={Slice2DemoSeed.SwimmingId}"); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.Equal("السباحة", x.GetProperty("sport").GetString())); }
    [Fact] public async Task Financial_report_filters_by_branch() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, $"/api/v1/reports/financial?from=2026-01-01&to=2026-12-31&branchId={Slice2DemoSeed.CityBranchId}"); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.Equal("فرع مدينة نصر", x.GetProperty("branch").GetString())); }
    [Fact] public async Task Financial_report_filters_by_plan() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, $"/api/v1/reports/financial?from=2026-01-01&to=2026-12-31&planId={Slice3DemoSeed.SwimmingMonthlyId}"); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.Equal("اشتراك سباحة شهري", x.GetProperty("plan").GetString())); }
    [Fact] public async Task Academy_b_collection_never_appears() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/financial?from=2026-01-01&to=2026-12-31"); Assert.DoesNotContain("أكاديمية المستقبل", j.ToString()); }
    [Fact] public async Task Coach_cannot_access_financial_report() { using var c = await Staff(DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/reports/financial")).StatusCode); }
    [Fact] public async Task Guardian_cannot_access_financial_report() { using var c = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/reports/financial")).StatusCode); }
    [Fact] public async Task Financial_export_contains_filtered_confirmed_rows_only() { using var c = await Staff(DemoSeed.OwnerEmail); var csv = await c.GetStringAsync($"/api/v1/reports/financial/export?from=2026-01-01&to=2026-12-31&sportId={Slice2DemoSeed.SwimmingId}"); Assert.Contains("السباحة", csv); Assert.DoesNotContain("فاشل", csv); }
    [Fact] public async Task Financial_export_rows_correspond_to_report() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/financial?from=2026-01-01&to=2026-12-31"); var csv = await c.GetStringAsync("/api/v1/reports/financial/export?from=2026-01-01&to=2026-12-31"); Assert.Equal(j.GetProperty("totalCount").GetInt32(), csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1); }
    [Fact] public async Task Receipt_amount_equals_collection() { await using var s = Scope(); var db = Db(s); var r = await db.Receipts.Include(x => x.Collection).FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId); Assert.Equal(r.Collection.Amount, r.Amount); }
    [Fact] public async Task Guardian_can_read_own_authorized_receipt() { var id = await ReceiptId(); using var c = await Guardian(); Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/guardian/subscriptions/receipts/{id}")).StatusCode); }
    [Fact] public async Task Guardian_cannot_read_another_payers_receipt() { using var c = await Guardian(); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/guardian/subscriptions/receipts/{Guid.NewGuid()}")).StatusCode); }
    [Fact] public async Task Academy_b_receipt_is_unavailable() { using var c = await Staff(DemoSeed.FutureOwnerEmail); var id = await ReceiptId(); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/subscriptions/receipts/{id}")).StatusCode); }
    [Fact] public async Task Attendance_filters_by_month_and_year() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/attendance?month=9&year=2026"); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.StartsWith("2026-09", x.GetProperty("date").GetString())); }
    [Fact] public async Task Attendance_filters_by_sport() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, $"/api/v1/reports/attendance?month=9&year=2026&sportId={Slice2DemoSeed.FootballId}"); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.Equal("كرة القدم", x.GetProperty("sport").GetString())); }
    [Fact] public async Task Attendance_filters_by_branch() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, $"/api/v1/reports/attendance?month=9&year=2026&branchId={Slice2DemoSeed.CityBranchId}"); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.Equal("فرع مدينة نصر", x.GetProperty("branch").GetString())); }
    [Fact] public async Task Attendance_filters_by_group() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, $"/api/v1/reports/attendance?month=9&year=2026&groupId={Slice2DemoSeed.FootballGroupId}"); var names = j.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("group").GetString()).Distinct().ToArray(); Assert.Single(names); }
    [Fact] public async Task Birth_year_filter_returns_only_2019() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/attendance?month=9&year=2026&birthYear=2019"); Assert.NotEmpty(j.GetProperty("items").EnumerateArray()); Assert.All(j.GetProperty("items").EnumerateArray(), x => Assert.Equal(2019, x.GetProperty("birthYear").GetInt32())); }
    [Fact] public async Task Present_and_absent_statuses_are_correct() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/attendance?month=9&year=2026"); var states = j.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("status").GetString()).ToArray(); Assert.Contains("Present", states); Assert.Contains("Absent", states); }
    [Fact] public async Task Missing_row_is_not_silently_absent() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/attendance?month=9&year=2026"); Assert.Equal("StoredRecordsOnly", j.GetProperty("coverage").GetString()); Assert.Equal(await StoredPlayerAttendanceCount(), j.GetProperty("totalCount").GetInt32()); }
    [Fact] public async Task Staff_attendance_is_separate() { using var c = await Staff(DemoSeed.OwnerEmail); var j = await Get(c, "/api/v1/reports/attendance?subject=staff&month=9&year=2026"); Assert.All(j.GetProperty("items").EnumerateArray(), x => { Assert.True(x.TryGetProperty("staff", out _)); Assert.False(x.TryGetProperty("player", out _)); }); }
    [Fact] public async Task Coach_attendance_is_scoped_to_assigned_groups() { using var c = await Staff(DemoSeed.CoachEmail); var j = await Get(c, "/api/v1/reports/attendance?month=9&year=2026"); var names = j.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("group").GetString()).Distinct().ToArray(); Assert.Single(names); }
    [Fact] public async Task Attendance_export_respects_filters() { using var c = await Staff(DemoSeed.OwnerEmail); var csv = await c.GetStringAsync("/api/v1/reports/attendance/export?month=9&year=2026&birthYear=2019"); Assert.Contains("يوسف خالد سمير", csv); Assert.DoesNotContain("NG-0001", csv); }
    [Fact] public async Task Attendance_export_has_tenant_isolation() { using var c = await Staff(DemoSeed.OwnerEmail); var csv = await c.GetStringAsync("/api/v1/reports/attendance/export?month=9&year=2026"); Assert.DoesNotContain("Future", csv, StringComparison.OrdinalIgnoreCase); }
    [Fact] public void Csv_formula_injection_is_escaped() { var method = typeof(ReportingEndpoints).GetMethod("CsvCell", BindingFlags.NonPublic | BindingFlags.Static)!; Assert.Equal("\"'=SUM(1,1)\"", method.Invoke(null, new object?[] { "=SUM(1,1)" })); }
    [Fact] public async Task Demo_rerun_does_not_duplicate_reporting_sources() { await using var s = Scope(); var db = Db(s); var before = await db.PlayerAttendances.CountAsync(x => x.Id == Slice8DemoSeed.ReportAttendanceId); var owner = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Role == AcademyRole.AcademyOwner).Select(x => x.UserId).SingleAsync(); await Slice8DemoSeed.SeedAsync(db, owner, new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), default); Assert.Equal(before, await db.PlayerAttendances.CountAsync(x => x.Id == Slice8DemoSeed.ReportAttendanceId)); }
    [Fact] public async Task Dashboard_report_and_receipts_are_consistent()
    {
        using var c = await Staff(DemoSeed.OwnerEmail);
        var dashboard = await Get(c, "/api/v1/reports/owner-summary?from=2026-01-01&to=2026-12-31");
        var report = await Get(c, "/api/v1/reports/financial?from=2026-01-01&to=2026-12-31");
        Assert.Equal(dashboard.GetProperty("collections").GetProperty("total").GetDecimal(), report.GetProperty("netAmount").GetDecimal());
        Assert.Equal(await NetRevenue(), report.GetProperty("netAmount").GetDecimal());
        await using var s = Scope(); var db = Db(s);
        var collections = db.Collections.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.ConfirmedAtUtc >= new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero) && x.ConfirmedAtUtc < new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero));
        Assert.Equal(await collections.SumAsync(x => x.Amount), await db.Receipts.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && collections.Select(c => c.Id).Contains(x.CollectionId)).SumAsync(x => x.Amount));
        var gross = await collections.SumAsync(x => x.Amount)
            + (await db.ProductSales.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Status == "Confirmed" && x.ConfirmedAtUtc >= new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero) && x.ConfirmedAtUtc < new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero)).SumAsync(x => (decimal?)x.Amount) ?? 0)
            + await ConfirmedTournamentRevenue(db);
        Assert.Equal(gross, report.GetProperty("totalAmount").GetDecimal());
    }
    [Fact] public async Task Content_areas_produce_no_financial_total() { var before = await CollectionSum(); using var c = await Guardian(); _ = await c.GetAsync("/api/v1/guardian/catalog"); _ = await c.GetAsync("/api/v1/guardian/nutrition?category=Breakfast"); Assert.Equal(before, await CollectionSum()); }

    [Fact]
    public async Task Confirmed_tournament_fee_is_revenue_but_pending_fee_is_not()
    {
        using var client = await Staff(DemoSeed.OwnerEmail);
        const string financial = "/api/v1/reports/financial?from=2026-01-01&to=2026-12-31";
        var before = await Get(client, financial);
        await using var scope = Scope(); var db = Db(scope);
        var enrollment = await db.SportEnrollments.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId);
        var pendingEnrollment = await db.SportEnrollments.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.SportId == enrollment.SportId && x.Id != enrollment.Id);
        var actor = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Role == AcademyRole.AcademyOwner).Select(x => x.UserId).SingleAsync();
        var tournamentId = Guid.NewGuid(); var confirmedId = Guid.NewGuid(); var pendingId = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var receiptCount = await db.Receipts.CountAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId);
        try
        {
            db.AcademyTournaments.Add(new AcademyTournament { Id = tournamentId, AcademyId = DemoSeed.NogoomAcademyId, Name = "دوري اختبار التقارير", SportId = enrollment.SportId, Fee = 250, Currency = "EGP", CreatedAtUtc = time, UpdatedAtUtc = time });
            db.TournamentPayments.AddRange(
                new TournamentPayment { Id = confirmedId, AcademyId = DemoSeed.NogoomAcademyId, TournamentId = tournamentId, EnrollmentId = enrollment.Id, RequestedByUserId = actor, TournamentName = "دوري اختبار التقارير", Amount = 250, Currency = "EGP", IdempotencyKey = confirmedId.ToString(), Status = "Confirmed", PaymentMethod = "Cash", ConfirmedByUserId = actor, ConfirmedAtUtc = time, ReceiptNumber = "TOURNAMENT-TEST-" + confirmedId, CreatedAtUtc = time, UpdatedAtUtc = time },
                new TournamentPayment { Id = pendingId, AcademyId = DemoSeed.NogoomAcademyId, TournamentId = tournamentId, EnrollmentId = pendingEnrollment.Id, RequestedByUserId = actor, TournamentName = "دوري اختبار التقارير", Amount = 900, Currency = "EGP", IdempotencyKey = pendingId.ToString(), Status = "Pending", CreatedAtUtc = time, UpdatedAtUtc = time });
            await db.SaveChangesAsync();
            var after = await Get(client, financial);
            Assert.Equal(before.GetProperty("totalAmount").GetDecimal() + 250, after.GetProperty("totalAmount").GetDecimal());
            Assert.Equal(before.GetProperty("netAmount").GetDecimal() + 250, after.GetProperty("netAmount").GetDecimal());
            var dashboard = await Get(client, "/api/v1/reports/owner-summary?from=2026-01-01&to=2026-12-31");
            Assert.Equal(after.GetProperty("netAmount").GetDecimal(), dashboard.GetProperty("collections").GetProperty("total").GetDecimal());
            Assert.Equal(await NetRevenue(), after.GetProperty("netAmount").GetDecimal());
            Assert.Equal(receiptCount, await db.Receipts.CountAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId));
        }
        finally
        {
            await db.TournamentPayments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && (x.Id == confirmedId || x.Id == pendingId)).ExecuteDeleteAsync();
            await db.AcademyTournaments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Id == tournamentId).ExecuteDeleteAsync();
        }
    }

    private async Task AssertPaymentStatusNotRevenue(PaymentRequestStatus status) { await using var s = Scope(); var db = Db(s); var payment = await db.PaymentRequests.FirstOrDefaultAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Status == status); if (payment is not null) Assert.False(await db.Collections.AnyAsync(x => x.PaymentRequestId == payment.Id)); else Assert.Equal(0, await db.PaymentRequests.CountAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Status == status)); }
    private async Task<decimal> CollectionSum() { await using var s = Scope(); return await Db(s).Collections.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId).SumAsync(x => x.Amount); }
    private async Task<decimal> NetRevenue()
    {
        await using var s = Scope(); var db = Db(s);
        var start = new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero);
        var ids = db.Collections.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.ConfirmedAtUtc >= start && x.ConfirmedAtUtc < end).Select(x => x.Id);
        return (await db.Collections.Where(x => ids.Contains(x.Id)).SumAsync(x => (decimal?)x.Amount) ?? 0)
            - (await db.CollectionRefunds.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && ids.Contains(x.CollectionId)).SumAsync(x => (decimal?)x.Amount) ?? 0)
            + (await db.ProductSales.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Status == "Confirmed" && x.ConfirmedAtUtc >= start && x.ConfirmedAtUtc < end).SumAsync(x => (decimal?)x.Amount) ?? 0)
            + await ConfirmedTournamentRevenue(db);
    }
    private static async Task<decimal> ConfirmedTournamentRevenue(FoundationDbContext db)
    {
        var start = new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero);
        return await db.TournamentPayments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Status == "Confirmed"
            && x.Currency == "EGP" && x.ConfirmedAtUtc >= start && x.ConfirmedAtUtc < end
            && db.SportEnrollments.Any(e => e.AcademyId == x.AcademyId && e.Id == x.EnrollmentId))
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
    }
    private async Task<int> StoredPlayerAttendanceCount() { await using var s = Scope(); return await Db(s).PlayerAttendances.CountAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.TrainingSession.SessionDate >= new DateOnly(2026, 9, 1) && x.TrainingSession.SessionDate <= new DateOnly(2026, 9, 30) && x.TrainingSession.Status != Academy.Infrastructure.Attendance.TrainingSessionStatus.Cancelled); }
    private async Task<Guid> ReceiptId() { await using var s = Scope(); return await Db(s).Receipts.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId).Select(x => x.Id).FirstAsync(); }
    private AsyncServiceScope Scope() => factory.Services.CreateAsyncScope();
    private static FoundationDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); if (email == DemoSeed.CoachEmail) await MobileCoachTestSession.SignInAsync(c, factory.Services); else (await Post(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c; }
    private async Task<HttpClient> Guardian() { var c = factory.CreateClient(); var req = await Post(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); var challenge = await req.Content.ReadFromJsonAsync<Challenge>(); (await Post(c, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge!.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" })).EnsureSuccessStatusCode(); return c; }
    private static async Task<JsonElement> Get(HttpClient c, string path) => await c.GetFromJsonAsync<JsonElement>(path);
    private static async Task<HttpResponseMessage> Post(HttpClient c, string path, object body) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", (await c.GetFromJsonAsync<Csrf>("/api/v1/auth/csrf"))!.Token); return await c.SendAsync(request); }
    private sealed record Csrf(string Token); private sealed record Challenge(Guid ChallengeId);
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection); builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true"); builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456"); } }
}
