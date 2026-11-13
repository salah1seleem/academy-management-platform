using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Historical_receipt_currency_and_amount_survive_exact_and_reordered_imports()
    {
        using var platform = await Platform();
        var ownerPhone = "01094" + Random.Shared.Next(100000, 999999);
        var academy = await Create(platform, ownerPhone);
        await EnableCorrectionImport(platform, academy);
        var book = await MultisportSample(platform, academy);
        var full = ImportXlsx.Write(book);
        var approved = await Preview(platform, academy, full);
        Assert.True(approved.CanApprove, string.Join(" / ", approved.Errors.Select(e => e.Message)));
        Assert.Equal(1, approved.HistoricalPayments);
        Assert.Equal(600m, approved.HistoricalPaymentTotal);
        Assert.Equal("EGP", approved.HistoricalPaymentCurrency);
        var published = await PublishCorrectionBatch(platform, academy, full, approved, historyApproved: true);
        Assert.Equal("EGP", published.GetProperty("result").GetProperty("historicalPaymentCurrency").GetString());
        using var replay = await Stage(platform, academy, full, approved, historyApproved: true);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replayed.GetProperty("replayedRequest").GetBoolean());
        Assert.Equal(published.GetProperty("id").GetGuid(), replayed.GetProperty("id").GetGuid());
        Assert.Equal(published.GetProperty("publishedRows").GetInt32(), replayed.GetProperty("publishedRows").GetInt32());
        Assert.Equal(600m, replayed.GetProperty("result").GetProperty("historicalPaymentTotal").GetDecimal());
        var branches = book.Sheet("الفروع").Rows;
        var reordered = ImportXlsx.Write(WithRows(book, "الفروع", [branches[0], .. branches.Skip(1).Reverse()]));
        Assert.False(full.SequenceEqual(reordered));
        var reorderedPreview = await Preview(platform, academy, reordered);
        Assert.True(reorderedPreview.CanApprove, string.Join(" / ", reorderedPreview.Errors.Select(e => e.Message)));
        var reorderedResult = await PublishCorrectionBatch(platform, academy, reordered, reorderedPreview, historyApproved: true);
        Assert.Equal(0, reorderedResult.GetProperty("publishedRows").GetInt32());
        Assert.Equal(51, reorderedResult.GetProperty("replayedRows").GetInt32());
        Assert.Equal(600m, reorderedResult.GetProperty("result").GetProperty("historicalPaymentTotal").GetDecimal());
        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collection = await db.Collections.IgnoreQueryFilters().SingleAsync(c => c.AcademyId == academy);
        var receipt = await db.Receipts.IgnoreQueryFilters().SingleAsync(r => r.AcademyId == academy);
        Assert.Equal(600m, collection.Amount);
        Assert.Equal(600m, receipt.Amount);
        Assert.Equal("EGP", receipt.Currency);
        Assert.Equal(collection.Id, receipt.CollectionId);
        Assert.Equal(2, await db.AcademyImportJobs.IgnoreQueryFilters().CountAsync(j => j.AcademyId == academy));
    }

    [Fact]
    public async Task Current_workbook_publishes_second_sport_prior_period_and_receipt_from_corrections()
    {
        using var platform = await Platform();
        var ownerPhone = "01094" + Random.Shared.Next(100000, 999999);
        var academy = await Create(platform, ownerPhone);
        await EnableCorrectionImport(platform, academy);
        var book = await MultisportSample(platform, academy);
        var originalPlayer = book.Sheet("اللاعبون").Rows.Single(row => row[0].Value == "PL01");
        var mixed = ImportXlsx.Write(BreakPlayerName(book, "PL01"));
        var preview = await Preview(platform, academy, mixed);
        Assert.Equal(new ImportCleanRows(49, 1, 5), preview.CleanRows);
        Assert.False(preview.CanApprove);
        await AssertNoImportWrites(academy);

        using var cleanFile = await DownloadPlayerCorrections(platform, academy, mixed, preview.FileHash, clean: true);
        using var correctionFile = await DownloadPlayerCorrections(platform, academy, mixed, preview.FileHash);
        cleanFile.EnsureSuccessStatusCode();
        correctionFile.EnsureSuccessStatusCode();
        var clean = await cleanFile.Content.ReadAsByteArrayAsync();
        var correction = ImportXlsx.Read(await correctionFile.Content.ReadAsByteArrayAsync());
        Assert.Equal("EN-SW-PL01", correction.Sheet("التسجيلات").Rows[1][0].Value);
        Assert.Equal("PER-SW-PREV", correction.Sheet("فترات سابقة اختيارية").Rows[1][0].Value);
        var cleanPreview = await Preview(platform, academy, clean);
        Assert.True(cleanPreview.CanApprove, string.Join(" / ", cleanPreview.Errors.Select(e => e.Message)));
        Assert.Equal(0, cleanPreview.HistoricalPayments);
        Assert.Equal(49, (await PublishCorrectionBatch(platform, academy, clean, cleanPreview)).GetProperty("publishedRows").GetInt32());

        var corrected = ImportXlsx.Write(WithRows(correction, "اللاعبون", [correction.Sheet("اللاعبون").Rows[0], originalPlayer]));
        var approved = await Preview(platform, academy, corrected);
        Assert.True(approved.CanApprove, string.Join(" / ", approved.Errors.Select(e => e.Message)));
        Assert.Equal((1, 2), (approved.PlayerCount, approved.RowCount));
        Assert.Equal(3, approved.Historical!.Periods);
        Assert.Equal(2, approved.Historical.Attendance);
        Assert.Equal(1, approved.HistoricalPayments);
        Assert.Equal(600m, approved.HistoricalPaymentTotal);
        Assert.Equal("EGP", approved.HistoricalPaymentCurrency);
        Assert.Equal("كرة القدم", approved.RegistrationLabels["EN-PL01"].Sport);
        Assert.Equal(new ImportRegistrationLabel("سباحة", "مجموعة السباحة للاختبار",
            approved.RegistrationLabels["EN-PL01"].Branch), approved.RegistrationLabels["EN-SW-PL01"]);
        var result = await PublishCorrectionBatch(platform, academy, corrected, approved, historyApproved: true);
        Assert.Equal(2, result.GetProperty("publishedRows").GetInt32());
        Assert.Equal("EGP", result.GetProperty("result").GetProperty("historicalPaymentCurrency").GetString());

        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.Players.IgnoreQueryFilters().SingleAsync(p => p.AcademyId == academy && p.PlayerCode == "PL01");
        Assert.Equal(new DateOnly(2026, 9, 1), player.RegisteredOn);
        var enrollments = await db.ImportedEnrollments.IgnoreQueryFilters().Where(e => e.AcademyId == academy && e.PlayerId == player.Id).ToDictionaryAsync(e => e.LegacyEnrollmentId);
        Assert.Equal(2, enrollments.Count);
        var footballId = enrollments["EN-PL01"].SportEnrollmentId;
        var swimmingId = enrollments["EN-SW-PL01"].SportEnrollmentId;
        var periods = await db.SubscriptionPeriods.IgnoreQueryFilters().Where(p => p.AcademyId == academy && (p.SportEnrollmentId == footballId || p.SportEnrollmentId == swimmingId)).ToListAsync();
        Assert.Equal(3, periods.Count);
        var football = Assert.Single(periods, p => p.SportEnrollmentId == footballId);
        var swimming = Assert.Single(periods, p => p.SportEnrollmentId == swimmingId && p.StartDate.Month == 10);
        var previous = Assert.Single(periods, p => p.SportEnrollmentId == swimmingId && p.StartDate.Month == 9);
        Assert.Equal((12, 11), (football.InitialSessions, football.RemainingSessions));
        Assert.Equal((8, 8), (swimming.InitialSessions, swimming.RemainingSessions));
        Assert.Equal((8, 7), (previous.InitialSessions, previous.RemainingSessions));
        var collection = await db.Collections.IgnoreQueryFilters().SingleAsync(c => c.AcademyId == academy);
        Assert.Equal(600m, collection.Amount);
        Assert.Equal(swimmingId, collection.SportEnrollmentId);
        var evidence = await db.ImportedHistoricalPayments.IgnoreQueryFilters().SingleAsync(e => e.AcademyId == academy);
        Assert.Equal(previous.Id, evidence.SubscriptionPeriodId);
        Assert.Equal(collection.Id, evidence.CollectionId);
        var receipt = await db.Receipts.IgnoreQueryFilters().SingleAsync(r => r.AcademyId == academy);
        Assert.Equal(collection.Id, receipt.CollectionId);
        Assert.Equal(600m, receipt.FinalAmount);
        var moves = await db.SubscriptionSessionMovements.IgnoreQueryFilters()
            .Where(m => m.AcademyId == academy && (m.SubscriptionPeriodId == football.Id || m.SubscriptionPeriodId == previous.Id || m.SubscriptionPeriodId == swimming.Id))
            .Select(m => new { m.SubscriptionPeriodId, m.Quantity }).ToListAsync();
        Assert.Equal(2, moves.Count);
        Assert.Contains(moves, m => m.SubscriptionPeriodId == football.Id && m.Quantity == -1);
        Assert.Contains(moves, m => m.SubscriptionPeriodId == previous.Id && m.Quantity == -1);
        Assert.Equal(50, await db.Players.IgnoreQueryFilters().CountAsync(p => p.AcademyId == academy));
        Assert.Equal(51, await db.SportEnrollments.IgnoreQueryFilters().CountAsync(e => e.AcademyId == academy));
        Assert.Equal(5, await db.PlayerAttendances.IgnoreQueryFilters().CountAsync(a => a.AcademyId == academy));
        var ownerEmail = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.Role == AcademyRole.AcademyOwner)
            .Select(m => m.User.Email).SingleAsync();
        var guardianPhone = await db.GuardianPlayerLinks.Where(l => l.AcademyId == academy && l.PlayerId == player.Id)
            .Select(l => l.Guardian.User.PhoneNumber).SingleAsync();
        var otherPhone = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber != guardianPhone)
            .Select(g => g.User.PhoneNumber).FirstAsync();
        var swimmingBranch = await db.SportEnrollments.IgnoreQueryFilters().Where(e => e.AcademyId == academy && e.Id == swimmingId)
            .Select(e => e.BranchId).SingleAsync();
        var ledgerScope = $"sportId={swimming.SportId}&branchId={swimmingBranch}&planId={receipt.SubscriptionPlanId}&collectionType=Subscription";
        await VerifyHistoricalReceipt(platform, academy, receipt.Id, ownerPhone, ownerEmail!, guardianPhone!, otherPhone!, ledgerScope);
    }

    private async Task VerifyHistoricalReceipt(HttpClient platform, Guid academy, Guid receipt, string ownerPhone, string ownerEmail, string guardianPhone, string otherPhone, string ledgerScope)
    {
        var setupPath = $"/api/v1/platform/academies/{academy}";
        var detail = await platform.GetFromJsonAsync<JsonElement>(setupPath + "/setup");
        var version = detail.GetProperty("profile").GetProperty("editVersion").GetInt32();
        (await Send(platform, setupPath + "/setup", new PlatformSetup.SetupWrite(version, "أكاديمية اختبار الإيصال", "موقع اصطناعي",
            ownerPhone, ownerPhone, "Africa/Cairo", "Trial", 0, "EGP", "Monthly", new(2026, 9, 1), new(2027, 1, 1)), true)).EnsureSuccessStatusCode();
        var entitlements = await platform.GetFromJsonAsync<JsonElement>(setupPath + "/entitlements");
        var features = entitlements.EnumerateArray().ToDictionary(item => item.GetProperty("feature").GetString()!,
            item => item.GetProperty("feature").GetString() is "training" or "matches");
        (await Send(platform, setupPath + "/configuration", new { features }, true)).EnsureSuccessStatusCode();
        var csrf = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
        using (var form = new MultipartFormDataContent())
        {
            var image = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII="));
            image.Headers.ContentType = new("image/png");
            form.Add(image, "file", "logo.png");
            using var logo = new HttpRequestMessage(HttpMethod.Post, setupPath + "/logo") { Content = form };
            logo.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            (await platform.SendAsync(logo)).EnsureSuccessStatusCode();
        }
        detail = await platform.GetFromJsonAsync<JsonElement>(setupPath + "/setup");
        Assert.Empty(detail.GetProperty("readiness").EnumerateArray());
        (await Send(platform, setupPath + "/state", new { target = "Active", version = detail.GetProperty("profile").GetProperty("editVersion").GetInt32() }, true)).EnsureSuccessStatusCode();
        Assert.Contains("تفعيل الأكاديمية", (await platform.GetFromJsonAsync<JsonElement>(setupPath + "/setup")).GetProperty("timeZoneLockReason").GetString());

        async Task SignInGuardian(HttpClient client, string phone)
        {
            const string path = "/api/v1/auth/guardian/otp";
            using var challenge = await Send(client, path + "/request", new { phoneNumber = phone }, platform: false);
            Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
            var challengeId = (await challenge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
            (await Send(client, path + "/verify", new { phoneNumber = phone, challengeId, code = "246810" }, platform: false)).EnsureSuccessStatusCode();
        }

        using var owner = factory.CreateClient();
        using var guardian = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        (await Send(owner, "/api/v1/auth/login", new { email = ownerEmail, password = "Demo-Only-123!" }, platform: false)).EnsureSuccessStatusCode();
        await SignInGuardian(guardian, guardianPhone);
        await SignInGuardian(other, otherPhone);
        var receiptPath = $"/api/v1/guardian/subscriptions/receipts/{receipt}";
        foreach (var (client, path) in new[] { (owner, $"/api/v1/subscriptions/receipts/{receipt}"), (guardian, receiptPath) })
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(600m, data.GetProperty("finalAmount").GetDecimal());
            Assert.Equal("Cash", data.GetProperty("paymentMethod").GetString());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("providerReference").ValueKind);
            Assert.Equal("2026-09-01", data.GetProperty("historicalPaidOn").GetString());
            Assert.False(data.TryGetProperty("paidAtAcademy", out _));
            Assert.Equal("2026-09-01", data.GetProperty("historicalPeriod").GetProperty("startDate").GetString());
            Assert.Equal("2026-09-30", data.GetProperty("historicalPeriod").GetProperty("endDate").GetString());
            Assert.False(data.TryGetProperty("evidence", out _));
            Assert.False(data.TryGetProperty("canReviewVoid", out var canVoid) && canVoid.GetBoolean());
        }
        var ownerList = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/receipts?from=2026-09-01&to=2026-09-30&page=1&pageSize=1");
        var ownerRow = Assert.Single(ownerList.GetProperty("items").EnumerateArray());
        Assert.Equal(receipt, ownerRow.GetProperty("id").GetGuid());
        Assert.Equal("2026-09-01", ownerRow.GetProperty("historicalPaidOn").GetString());
        var familyList = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/subscriptions/receipts?page=1&allIssued=true");
        var familyRow = Assert.Single(familyList.GetProperty("items").EnumerateArray());
        Assert.Equal(receipt, familyRow.GetProperty("id").GetGuid());
        Assert.Equal("2026-09-01", familyRow.GetProperty("historicalPaidOn").GetString());
        Assert.Equal(JsonValueKind.Null, familyRow.GetProperty("paidAtAcademy").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(receiptPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(receiptPath)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync("/api/v1/reports/receipts?page=100001")).StatusCode);
        foreach (var (query, message) in new[] {
            ("from=2026-09-30&to=2026-09-01", "تاريخ البداية يجب أن يسبق النهاية أو يساويها."),
            ("from=2026-09-01&to=2027-10-01", "نطاق التاريخ يجب ألا يتجاوز 366 يومًا."),
            ("from=9999-12-31&to=9999-12-31", "اختر تاريخ نهاية صالحًا.") })
        {
            using var invalid = await owner.GetAsync("/api/v1/reports/receipts?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(message, Assert.Single(problem.GetProperty("errors").GetProperty("range").EnumerateArray()).GetString());
        }
        var dst = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/receipts?from=2026-04-24&to=2026-04-24");
        Assert.Equal(0, dst.GetProperty("totalCount").GetInt32());

        const string ledger = "/api/v1/reports/financial";
        var scope = "from=2026-09-01&to=2026-09-30&" + ledgerScope;
        var finance = await owner.GetFromJsonAsync<JsonElement>($"{ledger}?{scope}&provider=Cash");
        var entry = Assert.Single(finance.GetProperty("items").EnumerateArray());
        Assert.Equal(receipt, entry.GetProperty("receiptId").GetGuid());
        Assert.Equal(600m, entry.GetProperty("netAmount").GetDecimal());
        Assert.Equal("2026-09-01", entry.GetProperty("historicalPaidOn").GetString());
        Assert.Equal("", entry.GetProperty("providerReference").GetString());
        Assert.Equal(600m, finance.GetProperty("netAmount").GetDecimal());
        Assert.Equal(600m, Assert.Single(finance.GetProperty("sourceSummary").EnumerateArray(), row => row.GetProperty("key").GetString() == "Subscription").GetProperty("amount").GetDecimal());
        var csv = await owner.GetStringAsync($"{ledger}/export?{scope}&provider=Cash");
        Assert.Equal(2, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains($"\"{entry.GetProperty("receiptNumber").GetString()}\"", csv);
        Assert.Contains("\"Cash\",\"تحصيل سابق\",\"\",\"2026-09-01\",\"0.00\",\"600.00\",\"اشتراك\"", csv);
        Assert.DoesNotContain("HistoricalImport", csv);
        var electronic = await owner.GetFromJsonAsync<JsonElement>($"{ledger}?{scope}&provider=Online");
        Assert.Equal(0, electronic.GetProperty("totalCount").GetInt32());
        var later = await owner.GetFromJsonAsync<JsonElement>($"{ledger}?from=2026-09-02&to=2026-09-30&{ledgerScope}");
        Assert.Equal(0, later.GetProperty("totalCount").GetInt32());
        var august = await owner.GetFromJsonAsync<JsonElement>($"{ledger}?from=2026-08-31&to=2026-08-31&{ledgerScope}");
        Assert.Equal(0, august.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync($"{ledger}?{scope}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync($"{ledger}/export?{scope}")).StatusCode);
    }

    [Theory]
    [InlineData("فترات سابقة اختيارية", 3, "2008-09-01", "البداية")]
    [InlineData("التسجيلات", 7, "2008-09-01", "بداية الاشتراك")]
    [InlineData("فترات سابقة اختيارية", 4, "2026-10-01", "النهاية")]
    [InlineData("فترات سابقة اختيارية", 3, "2026-11-01", "البداية")]
    [InlineData("حضور اختياري", 4, "PER-SW-NOW", "كود الفترة")]
    [InlineData("تحصيلات سابقة اختيارية", 3, "601", "المبلغ")]
    public async Task Current_workbook_rejects_invalid_multisport_history_at_its_source_row(string sheet, int column, string value, string field)
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01093" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var book = await MultisportSample(platform, academy);
        var rows = book.Sheet(sheet).Rows;
        var invalid = ImportXlsx.Write(WithRows(book, sheet, [.. rows.SkipLast(1), ReplaceCell(rows[^1], column, value)]));
        var preview = await Preview(platform, academy, invalid);
        Assert.False(preview.CanApprove);
        Assert.Contains(preview.Errors, e => e.Sheet == sheet && e.Row == rows.Count && e.ColumnLabel.Contains(field));
        await AssertNoImportWrites(academy);
    }

    [Fact]
    public async Task Current_workbook_uses_earlier_secondary_start_and_requires_the_receipt_approval()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01092" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var book = await MultisportSample(platform, academy);
        var full = ImportXlsx.Write(book);
        var withPayment = await Preview(platform, academy, full);
        Assert.True(withPayment.CanApprove, string.Join(" / ", withPayment.Errors.Select(e => e.Message)));
        using var denied = await Stage(platform, academy, full, withPayment);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);
        await AssertNoImportWrites(academy);

        foreach (var sheet in new[] { "فترات سابقة اختيارية", "تحصيلات سابقة اختيارية" })
            book = WithRows(book, sheet, [book.Sheet(sheet).Rows[0]]);
        var attendance = book.Sheet("حضور اختياري").Rows;
        book = WithRows(book, "حضور اختياري", attendance.Take(attendance.Count - 1).ToArray());
        var registrations = book.Sheet("التسجيلات").Rows;
        book = WithRows(book, "التسجيلات", [registrations[0], ReplaceCell(registrations[1], 7, "2026-09-01")]);
        var earlier = ImportXlsx.Write(book);
        var approved = await Preview(platform, academy, earlier);
        Assert.True(approved.CanApprove, string.Join(" / ", approved.Errors.Select(e => e.Message)));
        Assert.Equal(51, (await PublishCorrectionBatch(platform, academy, earlier, approved)).GetProperty("publishedRows").GetInt32());
        Assert.Contains("حصص فعلية", (await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/academies/{academy}/setup")).GetProperty("timeZoneLockReason").GetString());
        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(new DateOnly(2026, 9, 1), await db.Players.IgnoreQueryFilters()
            .Where(p => p.AcademyId == academy && p.PlayerCode == "PL01").Select(p => p.RegisteredOn).SingleAsync());
    }

    private static async Task<ImportWorkbook> MultisportSample(HttpClient platform, Guid academy)
    {
        var book = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample"));
        var coaches = book.Sheet("المدربون").Rows;
        book = WithRows(book, "المدربون", [coaches[0], ReplaceCell(coaches[1], 4, "SP01,SP02"), .. coaches.Skip(2)]);
        void Add(string sheet, params string[] cells) =>
            book = WithRows(book, sheet, [.. book.Sheet(sheet).Rows, ConnectedImportSchema.Row(cells)]);
        Add("الرياضات", "SP02", "سباحة", "سباحة", "");
        Add("المجموعات", "GR-SW", "مجموعة السباحة للاختبار", "BR01", "SP02", "AG01", "20", "GROUP", "CO01", "");
        Add("المواعيد الأسبوعية", "SCH-SW", "GR-SW", "6", "08:00", "09:00", "2026-09-01", "2026-11-30");
        Add("الباقات", "PK-SW", "سباحة شهرية للاختبار", "SP02", "BR01", "مزدوجة", "600", "", "8", "GROUP", "", "1");
        Add("التسجيلات", "EN-SW-PL01", "PL01", "BR01", "SP02", "GR-SW", "PK-SW", "PER-SW-NOW", "2026-10-01", "2026-10-31", "8", "8", "600", "EGP");
        Add("فترات سابقة اختيارية", "PER-SW-PREV", "EN-SW-PL01", "PK-SW", "2026-09-01", "2026-09-30", "8", "7", "600", "EGP");
        Add("حصص فعلية اختيارية", "SES-SW-PREV", "GR-SW", "2026-09-12", "08:00", "09:00", "منعقدة");
        Add("حضور اختياري", "AT-SW-PREV", "SES-SW-PREV", "EN-SW-PL01", "حاضر", "PER-SW-PREV");
        Add("تحصيلات سابقة اختيارية", "PAY-SW-PREV", "PER-SW-PREV", "2026-09-01", "600", "EGP", "نقدي", "QA-SW-PREV-600");
        return book;
    }
}
