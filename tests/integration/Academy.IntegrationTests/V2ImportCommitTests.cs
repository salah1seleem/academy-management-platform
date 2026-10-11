using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Importing;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    private sealed record ImportCase(ImportWorkbook Book, Guid Branch, Guid Group, Guid Plan, int BirthYear);
    private async Task<ImportCase> ImportFixture(HttpClient owner, int count = 1, bool mixed = false, bool opening = false, bool unknown = false, string? phone = null, string? guardianName = null)
    {
        var f = await Fixture(false); var plan = await CreatePlan(owner, f.A);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await db.TrainingGroups.SingleAsync(x => x.Id == f.Group); group.Capacity = count + 20;
        var footballGroup = Guid.NewGuid(); var footballPlan = Guid.NewGuid();
        if (mixed)
        {
            db.TrainingGroups.Add(new() { Id = footballGroup, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "كرة قدم استيراد صناعية " + footballGroup.ToString("N")[..8], BranchId = f.A, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = group.AgeCategoryId, Capacity = 20 });
            db.SubscriptionPlans.Add(new() { Id = footballPlan, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "باقة استيراد كرة قدم صناعية " + footballPlan.ToString("N")[..8], SportId = Slice2DemoSeed.FootballId, PlanType = SubscriptionPlanType.Combined, DurationDays = 30, SessionCount = 8, Price = 600, Currency = "EGP", AllBranches = true, Revision = 1 });
        }
        await db.SaveChangesAsync();
        var book = ImportSource(ImportXlsx.Read(await owner.GetByteArrayAsync("/api/v1/people/import/template")), "test-" + Guid.NewGuid().ToString("N"));
        var rows = new List<IReadOnlyList<ImportCell>> { book.Sheet("Enrollments").Rows[0] };
        var phoneBase = Random.Shared.Next(10000000, 89998000);
        for (var i = 0; i < count; i++)
        {
            var child = mixed && i == 2 ? 0 : i; var family = mixed && i < 3 ? 0 : i;
            rows.Add(ImportCells("P" + child, "E" + i, "H" + family, "طفل استيراد صناعي " + child, guardianName ?? "ولي استيراد صناعي " + family, phone ?? "012" + (phoneBase + family).ToString("00000000"), f.BirthYear + "-01-01", f.A.ToString(), (mixed && i == 2 ? Slice2DemoSeed.FootballId : Slice2DemoSeed.SwimmingId).ToString(), "", (mixed && i == 2 ? footballGroup : f.Group).ToString(), (mixed && i == 2 ? footballPlan : plan).ToString(), "2026-09-01", "2026-09-30", "8", "5", unknown && i == 0 ? "" : "agreed_net", "", "", unknown && i == 0 ? "" : "540.00", "EGP", "existing", "بيانات اختبار اصطناعية"));
        }
        book = new(book.Sheets.Select(s => s.Name == "Enrollments" ? s with { Rows = rows } : opening && s.Name == "OpenBalances" ? s with { Rows = [s.Rows[0], ImportCells("E0", "500.00", "EGP", "2026-09-28", "OB-DEBT", "سند اعتماد اصطناعي 500"), ImportCells("E0", "-100.00", "EGP", "2026-09-28", "OB-CREDIT", "سند رصيد دائن اصطناعي")] } : s).ToArray());
        return new(book, f.A, f.Group, plan, f.BirthYear);
    }
    private static async Task<ImportPreviewResult> ImportCheck(HttpClient c, ImportWorkbook book)
    {
        var r = await PreviewFile(c, ImportXlsx.Write(book)); r.EnsureSuccessStatusCode();
        var preview = (await r.Content.ReadFromJsonAsync<ImportPreviewResult>())!;
        Assert.True(preview.CanApprove, string.Join(';', preview.Errors.Select(e => e.Column + ": " + e.Message))); return preview;
    }
    private static async Task<HttpResponseMessage> ImportStage(HttpClient c, ImportWorkbook book, ImportPreviewResult p, string? key = null, ImportGuardianResolution[]? resolutions = null, bool approvals = true, bool csrf = true)
    {
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(ImportXlsx.Write(book)), "file", "synthetic.xlsx");
        foreach (var item in new Dictionary<string, string> { ["ExpectedFileHash"] = p.FileHash, ["ExpectedReferenceStamp"] = p.ReferenceStamp, ["GuardianLinksApproved"] = approvals.ToString().ToLowerInvariant(), ["OpeningBalancesApproved"] = approvals.ToString().ToLowerInvariant(), ["AcceptFinancialReview"] = approvals.ToString().ToLowerInvariant(), ["GuardianResolutions"] = JsonSerializer.Serialize(resolutions ?? [], new JsonSerializerOptions(JsonSerializerDefaults.Web)) }) form.Add(new StringContent(item.Value), item.Key);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/people/import/commit") { Content = form }; request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());
        return await c.SendAsync(request);
    }
    private IServiceScopeFactory ImportScopes => factory.Services.GetRequiredService<IServiceScopeFactory>();
    private async Task ImportPublish(Guid id)
    {
        Assert.Equal(id, await ImportWorker.Claim(ImportScopes, TimeProvider.System));
        await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id);
    }
    private static async Task<Guid> ImportId(HttpResponseMessage response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    [Fact]
    public async Task IMP_02_04_05_atomic_1000_rows_preserve_multisport_siblings_agreed_net_and_signed_opening_without_cash()
    {
        using var owner = await Staff(); var f = await ImportFixture(owner, 1000, mixed: true, opening: true); var p = await ImportCheck(owner, f.Book);
        Assert.Equal((1000, 999, 998), (p.RowCount, p.PlayerCount, p.HouseholdCount));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = (await db.Players.CountAsync(), await db.GuardianProfiles.CountAsync(), await db.Collections.CountAsync(), await db.Receipts.CountAsync(), await db.PaymentRequests.CountAsync(), await db.ReceivableDocuments.CountAsync());
        var key = Guid.NewGuid().ToString(); var id = await ImportId(await ImportStage(owner, f.Book, p, key));
        Assert.Equal(before.Item1, await db.Players.CountAsync()); await ImportPublish(id);
        db.ChangeTracker.Clear(); var job = await db.AcademyImportJobs.SingleAsync(j => j.Id == id);
        Assert.Equal((ImportJobState.Completed, 1000, 0), (job.State, job.PublishedRows, job.ReplayedRows)); Assert.Null(job.StagedFile);
        Assert.Equal((before.Item1 + 999, before.Item2 + 998, before.Item3, before.Item4, before.Item5, before.Item6 + 2), (await db.Players.CountAsync(), await db.GuardianProfiles.CountAsync(), await db.Collections.CountAsync(), await db.Receipts.CountAsync(), await db.PaymentRequests.CountAsync(), await db.ReceivableDocuments.CountAsync()));
        var maps = await db.ImportedEnrollments.Where(m => m.JobId == id).ToListAsync(); Assert.Equal(maps.Single(x => x.LegacyEnrollmentId == "E0").PlayerId, maps.Single(x => x.LegacyEnrollmentId == "E2").PlayerId); Assert.Equal(maps.Single(x => x.LegacyEnrollmentId == "E0").GuardianId, maps.Single(x => x.LegacyEnrollmentId == "E1").GuardianId);
        var periods = await db.SubscriptionPeriods.Where(x => x.ImportJobId == id).ToListAsync(); Assert.All(periods, x => { Assert.Equal(SubscriptionOrigin.ImportedHistorical, x.Origin); Assert.Null(x.CollectionId); Assert.Equal(540m, x.PriceSnapshot); Assert.Equal(5, x.RemainingSessions); Assert.Equal(8, x.InitialSessions); });
        var documents = await db.ReceivableDocuments.Where(x => x.ImportJobId == id).ToListAsync(); Assert.Equal(new[] { -100m, 500m }, documents.Select(x => x.Amount).Order()); Assert.All(documents, x => Assert.Equal(ReceivableSource.OpeningBalance, x.Source)); using var snapshot = JsonDocument.Parse(maps.Single(x => x.LegacyEnrollmentId == "E0").AgreementSnapshot); Assert.Contains(snapshot.RootElement.GetProperty("openingBalances").EnumerateArray(), b => b.GetProperty("ApprovalEvidence").GetString() == "سند اعتماد اصطناعي 500");
        var guardianId = maps.Single(x => x.LegacyEnrollmentId == "E0").GuardianId; var guardian = await db.GuardianProfiles.Include(g => g.User).SingleAsync(g => g.Id == guardianId); Assert.False(guardian.User.PhoneNumberConfirmed); Assert.Null(guardian.User.PasswordHash);
        var balanceReport = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables/?playerId={maps.Single(x => x.LegacyEnrollmentId == "E0").PlayerId}"); Assert.Equal("500.00", balanceReport.GetProperty("outstanding").GetString()); Assert.Equal("100.00", balanceReport.GetProperty("overpaymentCredit").GetString());
        Assert.Equal(id, await ImportId(await ImportStage(owner, f.Book, p, key))); Assert.Equal(id, await ImportId(await ImportStage(owner, f.Book, p)));
        var records = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/import/jobs/{id}/records"); Assert.Equal(1000, records.GetArrayLength());
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/people/import/jobs/{id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/people/import/jobs/{id}/records")).StatusCode);
        await using var tx = await db.Database.BeginTransactionAsync(); var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPeriods\" SET \"PriceSnapshot\" = 0 WHERE \"Id\" = {periods[0].Id}")); Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task IMP_03_lease_recovery_and_regenerated_file_source_ids_do_not_duplicate()
    {
        using var owner = await Staff(); var f = await ImportFixture(owner, 3); var p = await ImportCheck(owner, f.Book); var id = await ImportId(await ImportStage(owner, f.Book, p));
        Assert.Equal(id, await ImportWorker.Claim(ImportScopes, TimeProvider.System));
        await using (var s = factory.Services.CreateAsyncScope()) { var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); var j = await db.AcademyImportJobs.SingleAsync(x => x.Id == id); j.LeaseUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); }
        await ImportPublish(id);
        var latest = ImportSource(ImportXlsx.Read(await owner.GetByteArrayAsync("/api/v1/people/import/template")), p.SourceSystem!);
        latest = new(latest.Sheets.Select(s => s.Name == "Enrollments" ? f.Book.Sheet("Enrollments") : s).ToArray()); var repeated = await ImportCheck(owner, latest); Assert.Empty(repeated.GuardianMatches);
        var second = await ImportId(await ImportStage(owner, latest, repeated)); Assert.NotEqual(id, second); await ImportPublish(second);
        var records = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/import/jobs/{second}/records"); Assert.Equal(3, records.GetArrayLength());
        await using var verify = factory.Services.CreateAsyncScope(); var v = verify.ServiceProvider.GetRequiredService<FoundationDbContext>(); var job = await v.AcademyImportJobs.SingleAsync(x => x.Id == second); Assert.Equal((0, 3), (job.PublishedRows, job.ReplayedRows)); Assert.Equal(3, await v.ImportedEnrollments.CountAsync(x => x.SourceSystem == p.SourceSystem)); Assert.Equal(2, (await v.AcademyImportJobs.SingleAsync(x => x.Id == id)).Attempts);
    }

    [Fact]
    public async Task IMP_04_unknown_historical_net_is_null_not_free_or_a_receipt_and_can_renew_at_current_price()
    {
        using var owner = await Staff(); var f = await ImportFixture(owner, unknown: true); var p = await ImportCheck(owner, f.Book); Assert.Equal(1, p.FinanciallyIncomplete);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ImportStage(owner, f.Book, p, approvals: false)).StatusCode);
        var id = await ImportId(await ImportStage(owner, f.Book, p)); await ImportPublish(id);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var period = await db.SubscriptionPeriods.SingleAsync(x => x.ImportJobId == id); Assert.Null(period.PriceSnapshot); Assert.Null(period.CollectionId); Assert.False(await db.Receipts.AnyAsync(r => r.SportEnrollmentId == period.SportEnrollmentId));
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{period.Id}"); Assert.Equal(JsonValueKind.Null, detail.GetProperty("priceSnapshot").ValueKind); Assert.Equal(JsonValueKind.Null, detail.GetProperty("receiptId").ValueKind);
        var quote = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = period.SportEnrollmentId, subscriptionPlanId = f.Plan }); quote.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task IMP_06_existing_contact_requires_explicit_scoped_relationship_not_phone_only()
    {
        using var owner = await Staff(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var guardian = await db.GuardianProfiles.Include(g => g.User).SingleAsync(g => g.AcademyId == DemoSeed.NogoomAcademyId && g.User.PhoneNumber == DemoSeed.GuardianPhone);
        var f = await ImportFixture(owner, phone: guardian.User.PhoneNumber, guardianName: guardian.DisplayName); var p = await ImportCheck(owner, f.Book); Assert.Equal(new[] { guardian.Id }, Assert.Single(p.GuardianMatches).Candidates);
        Assert.Equal(HttpStatusCode.Conflict, (await ImportStage(owner, f.Book, p)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ImportStage(owner, f.Book, p, resolutions: [new("H:H0", Guid.NewGuid())])).StatusCode);
        var id = await ImportId(await ImportStage(owner, f.Book, p, resolutions: [new("H:H0", guardian.Id)])); await ImportPublish(id);
        Assert.Equal(guardian.Id, (await db.ImportedEnrollments.SingleAsync(x => x.JobId == id)).GuardianId);
    }

    [Fact]
    public async Task IMP_07_price_change_after_staging_stops_publication_and_preserves_no_partial_data()
    {
        using var owner = await Staff(); var f = await ImportFixture(owner); var p = await ImportCheck(owner, f.Book); var id = await ImportId(await ImportStage(owner, f.Book, p));
        (await Write(owner, $"/api/v1/subscriptions/plans/{f.Plan}", Body(f.Branch, "سعر متغير صناعي " + f.Plan.ToString("N")[..8], 950, 1), true)).EnsureSuccessStatusCode();
        Assert.Equal(id, await ImportWorker.Claim(ImportScopes, TimeProvider.System));
        await using (var scope = factory.Services.CreateAsyncScope()) { var ex = await Assert.ThrowsAsync<ImportRejected>(() => scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id)); Assert.Equal("stale", ex.Code); }
        await ImportWorker.Fail(ImportScopes, id, "stale", DateTimeOffset.UtcNow);
        await using var verify = factory.Services.CreateAsyncScope(); var db = verify.ServiceProvider.GetRequiredService<FoundationDbContext>(); var job = await db.AcademyImportJobs.SingleAsync(x => x.Id == id); Assert.Equal(ImportJobState.Failed, job.State); Assert.Null(job.StagedFile); Assert.False(await db.ImportedEnrollments.AnyAsync(x => x.JobId == id)); Assert.False(await db.ImportedHouseholds.AnyAsync(x => x.JobId == id));
    }

    [Fact]
    public async Task IMP_03_database_failure_rolls_back_people_relationships_periods_and_openings_then_retries_once()
    {
        using var owner = await Staff(); var f = await ImportFixture(owner, 3, opening: true); var p = await ImportCheck(owner, f.Book); var id = await ImportId(await ImportStage(owner, f.Book, p));
        await using var setup = factory.Services.CreateAsyncScope(); var db = setup.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = (await db.Players.CountAsync(), await db.GuardianProfiles.CountAsync(), await db.AcademyMemberships.CountAsync(), await db.SubscriptionPeriods.CountAsync(), await db.ReceivableDocuments.CountAsync());
        // A test-only database trigger, never an application fault switch; scoped to this exact synthetic source.
        await db.Database.ExecuteSqlRawAsync("CREATE OR REPLACE FUNCTION v2_test_import_reject() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"SourceSystem\" = current_setting('academy.test_import_source', true) THEN RAISE EXCEPTION 'synthetic rollback' USING ERRCODE='23514'; END IF; RETURN NEW; END $$");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER v2_test_import_reject BEFORE INSERT ON \"ImportedEnrollments\" FOR EACH ROW EXECUTE FUNCTION v2_test_import_reject()");
        try
        {
            Assert.Equal(id, await ImportWorker.Claim(ImportScopes, TimeProvider.System));
            await using (var attempt = factory.Services.CreateAsyncScope())
            {
                var workDb = attempt.ServiceProvider.GetRequiredService<FoundationDbContext>(); await workDb.Database.OpenConnectionAsync();
                await workDb.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('academy.test_import_source', {p.SourceSystem}, false)");
                try { await Assert.ThrowsAsync<DbUpdateException>(() => attempt.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id)); }
                finally { await workDb.Database.ExecuteSqlRawAsync("RESET academy.test_import_source"); }
            }
            Assert.Equal(before, (await db.Players.CountAsync(), await db.GuardianProfiles.CountAsync(), await db.AcademyMemberships.CountAsync(), await db.SubscriptionPeriods.CountAsync(), await db.ReceivableDocuments.CountAsync())); Assert.False(await db.ImportedEnrollments.AnyAsync(x => x.JobId == id)); Assert.False(await db.ImportedHouseholds.AnyAsync(x => x.JobId == id));
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER v2_test_import_reject ON \"ImportedEnrollments\""); await db.Database.ExecuteSqlRawAsync("DROP FUNCTION v2_test_import_reject()"); }
        await ImportWorker.Fail(ImportScopes, id, "storage", DateTimeOffset.UtcNow); await ImportPublish(id);
        db.ChangeTracker.Clear(); var completed = await db.AcademyImportJobs.SingleAsync(j => j.Id == id); Assert.Equal((ImportJobState.Completed, 3, 2), (completed.State, completed.PublishedRows, completed.Attempts)); Assert.Equal(3, await db.ImportedEnrollments.CountAsync(x => x.JobId == id)); Assert.Equal(2, await db.ReceivableDocuments.CountAsync(x => x.ImportJobId == id));
    }

    [Fact]
    public async Task IMP_06_commit_enforces_csrf_roles_expiry_and_explicit_approval_without_file_disclosure()
    {
        using var owner = await Staff(); var f = await ImportFixture(owner); var p = await ImportCheck(owner, f.Book);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportStage(owner, f.Book, p, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ImportStage(owner, f.Book, p, approvals: false)).StatusCode);
        using var guardian = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await ImportStage(guardian, f.Book, p)).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); Assert.Equal(HttpStatusCode.Forbidden, (await ImportStage(coach, f.Book, p)).StatusCode);
        var id = await ImportId(await ImportStage(owner, f.Book, p));
        var json = await (await owner.GetAsync($"/api/v1/people/import/jobs/{id}")).Content.ReadAsStringAsync(); Assert.DoesNotContain("stagedFile", json); Assert.DoesNotContain("guardianResolutions", json); Assert.DoesNotContain("guardianPhone", json);
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); var j = await db.AcademyImportJobs.SingleAsync(x => x.Id == id); j.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        Assert.Null(await ImportWorker.Claim(ImportScopes, TimeProvider.System)); db.ChangeTracker.Clear(); j = await db.AcademyImportJobs.SingleAsync(x => x.Id == id); Assert.Equal(ImportJobState.Expired, j.State); Assert.Null(j.StagedFile); Assert.False(await db.SubscriptionPeriods.AnyAsync(x => x.ImportJobId == id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IMP_07_capacity_or_staff_authority_change_rejects_whole_staged_batch(bool revoke)
    {
        using var owner = await Staff(); var f = await ImportFixture(owner, 3); var email = $"imp.{Guid.NewGuid():N}@example.test";
        var r = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري استيراد صناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار")); r.EnsureSuccessStatusCode(); var member = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var grants = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "players" or "subscriptions", m is "players" or "subscriptions", false, false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.Branch], grants), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        // An administrator receives only their own scoped reference stamp.
        var template = ImportSource(ImportXlsx.Read(await admin.GetByteArrayAsync("/api/v1/people/import/template")), ImportCommitService.Source(f.Book)!);
        var book = new ImportWorkbook(template.Sheets.Select(s => s.Name == "Enrollments" ? f.Book.Sheet("Enrollments") : s).ToArray()); var p = await ImportCheck(admin, book); var id = await ImportId(await ImportStage(admin, book, p));
        if (revoke) (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.Branch], grants.Select(g => g with { Add = false }).ToArray()), true)).EnsureSuccessStatusCode();
        else { await using var change = factory.Services.CreateAsyncScope(); var d = change.ServiceProvider.GetRequiredService<FoundationDbContext>(); var g = await d.TrainingGroups.SingleAsync(x => x.Id == f.Group); g.Capacity = 2; await d.SaveChangesAsync(); }
        Assert.Equal(id, await ImportWorker.Claim(ImportScopes, TimeProvider.System));
        await using (var attempt = factory.Services.CreateAsyncScope()) { var ex = await Assert.ThrowsAsync<ImportRejected>(() => attempt.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id)); Assert.Equal(revoke ? "authority" : "stale", ex.Code); }
        await ImportWorker.Fail(ImportScopes, id, revoke ? "authority" : "stale", DateTimeOffset.UtcNow);
        await using var verify = factory.Services.CreateAsyncScope(); var db = verify.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.ImportedEnrollments.AnyAsync(x => x.JobId == id)); Assert.False(await db.ImportedHouseholds.AnyAsync(x => x.JobId == id));
    }

    [Fact]
    public async Task IMP_06_branch_admin_can_publish_only_scoped_records_and_source_conflict_never_overwrites()
    {
        using var owner = await Staff(); var f = await ImportFixture(owner); var email = $"imp.{Guid.NewGuid():N}@example.test";
        var r = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري استيراد صناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار")); r.EnsureSuccessStatusCode(); var member = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var grants = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "players" or "subscriptions", m is "players" or "subscriptions", false, false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.Branch], grants), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        async Task<ImportWorkbook> ScopedBook(bool different = false) { var t = ImportSource(ImportXlsx.Read(await admin.GetByteArrayAsync("/api/v1/people/import/template")), ImportCommitService.Source(f.Book)!); return new(t.Sheets.Select(s => s.Name != "Enrollments" ? s : s with { Rows = f.Book.Sheet("Enrollments").Rows.Select((row, i) => i != 1 || !different ? row : row.Select((cell, col) => col == 19 ? new ImportCell("700.00", true) : cell).ToArray()).ToArray() }).ToArray()); }
        var book = await ScopedBook(); var p = await ImportCheck(admin, book); var id = await ImportId(await ImportStage(admin, book, p)); await ImportPublish(id);
        Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/v1/people/import/jobs/{id}/records")).EnumerateArray());
        var changed = await ScopedBook(true); var changedPreview = await ImportCheck(admin, changed); var retry = await ImportId(await ImportStage(admin, changed, changedPreview));
        Assert.Equal(retry, await ImportWorker.Claim(ImportScopes, TimeProvider.System)); await using (var attempt = factory.Services.CreateAsyncScope()) { var ex = await Assert.ThrowsAsync<ImportRejected>(() => attempt.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(retry)); Assert.Equal("conflict", ex.Code); }
        await ImportWorker.Fail(ImportScopes, retry, "conflict", DateTimeOffset.UtcNow);
        await using var verify = factory.Services.CreateAsyncScope(); var db = verify.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(540m, (await db.SubscriptionPeriods.SingleAsync(x => x.ImportJobId == id)).PriceSnapshot); Assert.Single(await db.ImportedEnrollments.Where(x => x.SourceSystem == p.SourceSystem).ToListAsync());
    }
}
