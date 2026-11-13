using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Academy.Api.Importing;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class PlatformSetupImportTests
{
    [Theory]
    [InlineData("wrong.xlsm", "synthetic", HttpStatusCode.UnprocessableEntity)]
    [InlineData("empty.xlsx", "", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("corrupt.xlsx", "synthetic", HttpStatusCode.UnprocessableEntity)]
    public async Task Platform_import_rejects_invalid_workbook_before_creating_records(
        string filename, string content, HttpStatusCode expected)
    {
        using var platform = await Platform();
        var id = await Create(platform, "01093" + Random.Shared.Next(100000, 999999));
        var csrf = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", filename);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/platform/academies/{id}/import/preview") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        using var response = await platform.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        await AssertNoImportWrites(id);
    }

    [Fact]
    public async Task Platform_import_rejects_stale_reference_with_a_remediation_code_and_no_writes()
    {
        using var platform = await Platform();
        var id = await Create(platform, "01094" + Random.Shared.Next(100000, 999999));
        var path = $"/api/v1/platform/academies/{id}";
        var licenses = await platform.GetFromJsonAsync<JsonElement>(path + "/entitlements");
        var features = licenses.EnumerateArray().ToDictionary(
            x => x.GetProperty("feature").GetString()!,
            x => x.GetProperty("feature").GetString() is "training" or "matches");
        using var license = await Send(platform, path + "/entitlements", new { planCode = "Trial", features }, true);
        license.EnsureSuccessStatusCode();
        var bytes = ImportXlsx.Write(ConnectedImportSchema.Template(id, "EGP",
            new DateOnly(2026, 10, 25), true, 2, "stale-" + id.ToString("N"), 4712600));
        var preview = await Preview(platform, id, bytes);
        Assert.True(preview.CanApprove, string.Join("; ", preview.Errors.Select(e => e.Message)));

        using var response = await Stage(platform, id, bytes, preview with { ReferenceStamp = "stale" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("stale", problem.GetProperty("code").GetString());
        await AssertNoImportWrites(id);
    }

    private async Task AssertNoImportWrites(Guid academy)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.AcademyImportJobs.IgnoreQueryFilters().AnyAsync(j => j.AcademyId == academy));
        Assert.False(await db.Players.IgnoreQueryFilters().AnyAsync(p => p.AcademyId == academy));
        Assert.False(await db.Collections.IgnoreQueryFilters().AnyAsync(c => c.AcademyId == academy));
    }

    [Fact]
    public async Task Current_workbook_separates_player_reference_and_period_errors_without_hiding_bad_rows()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01097" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var workbook = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample"));
        var players = workbook.Sheet("اللاعبون").Rows;
        int Column(string key) => Array.FindIndex(AcademyWorkbookSchema.Players, field => field.Key == key);
        string Header(string key) => AcademyWorkbookSchema.Players[Column(key)].Header;
        IReadOnlyList<ImportCell> Set(IReadOnlyList<ImportCell> row, params (string Field, string Value)[] changes)
        {
            var edited = row.ToArray();
            foreach (var (key, value) in changes) edited[Column(key)] = new(value, true);
            return edited;
        }
        ImportWorkbook Replace(string name, IReadOnlyList<IReadOnlyList<ImportCell>> rows) =>
            new(workbook.Sheets.Select(sheet => sheet.Name == name ? sheet with { Rows = rows } : sheet).ToArray());

        var broken = players.ToList();
        broken[1] = Set(players[1], ("Group", "NO-GROUP"));
        broken[2] = Set(players[2], ("First", ""));
        broken[3] = Set(players[3], ("Net", "-0.01"));
        broken[4] = Set(players[4], ("Remaining", "9999"));
        broken[5] = Set(players[5], ("End", "2001-01-01"));
        broken.RemoveAt(broken.Count - 1);
        while (broken.Count < 63) broken.Add([]);
        broken.Add(Set(players[^1], ("Currency", "USD")));
        var brokenBytes = ImportXlsx.Write(Replace("اللاعبون", broken));
        var preview = await Preview(platform, academy, brokenBytes);
        Assert.False(preview.CanApprove);
        Assert.Equal(new ImportCleanRows(44, 6, 4), preview.CleanRows);
        var expectedIssues = new[] { (2, "Group"), (3, "First"), (4, "Net"), (5, "Remaining"), (6, "End"), (64, "Currency") };
        Assert.Equal(expectedIssues.Select(issue => (issue.Item1, Header(issue.Item2))), preview.Errors.Where(error => error.Sheet == "اللاعبون").Select(error => (error.Row, error.Column)).Distinct().OrderBy(error => error.Row));
        Assert.All(preview.Errors.Where(error => error.Sheet != "اللاعبون"), error => Assert.Equal("حضور اختياري", error.Sheet));
        await AssertNoImportWrites(academy);

        using var cleanFile = await DownloadPlayerCorrections(platform, academy, brokenBytes, preview.FileHash, clean: true);
        using var correctionFile = await DownloadPlayerCorrections(platform, academy, brokenBytes, preview.FileHash);
        cleanFile.EnsureSuccessStatusCode();
        correctionFile.EnsureSuccessStatusCode();
        var cleanBytes = await cleanFile.Content.ReadAsByteArrayAsync();
        var correction = ImportXlsx.Read(await correctionFile.Content.ReadAsByteArrayAsync());
        var originals = new[] { players[1], players[2], players[3], players[4], players[5], players[^1] };
        Assert.Equal(originals.Select(row => row[0].Value), correction.Sheet("اللاعبون").Rows.Skip(1).Select(row => row[0].Value));
        Assert.Contains(correction.Sheet("ابدأ هنا").Rows, row => row[0].Value == "الصف 7 · الأصل 64" && row[1].Value.Contains(Header("Currency")));
        var corrected = ImportXlsx.Write(new ImportWorkbook(correction.Sheets.Select(sheet => sheet.Name == "اللاعبون"
            ? sheet with { Rows = [players[0], .. originals] } : sheet).ToArray()));
        var clean = await Preview(platform, academy, cleanBytes);
        Assert.True(clean.CanApprove);
        Assert.Equal(44, (await PublishCorrectionBatch(platform, academy, cleanBytes, clean)).GetProperty("publishedRows").GetInt32());
        var repaired = await Preview(platform, academy, corrected);
        Assert.True(repaired.CanApprove, string.Join(" | ", repaired.Errors.Select(error => error.Message)));
        Assert.Equal(6, (await PublishCorrectionBatch(platform, academy, corrected, repaired)).GetProperty("publishedRows").GetInt32());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(2, await db.AcademyImportJobs.CountAsync(job => job.AcademyId == academy));
        Assert.Equal(50, await db.Players.IgnoreQueryFilters().CountAsync(player => player.AcademyId == academy));
        Assert.Equal(50, await db.SportEnrollments.IgnoreQueryFilters().CountAsync(enrollment => enrollment.AcademyId == academy));
        Assert.False(await db.Collections.IgnoreQueryFilters().AnyAsync(collection => collection.AcademyId == academy));
        Assert.False(await db.Receipts.IgnoreQueryFilters().AnyAsync(receipt => receipt.AcademyId == academy));
        Assert.Equal(4, await db.PlayerAttendances.IgnoreQueryFilters().CountAsync(attendance => attendance.AcademyId == academy));
    }

    [Fact]
    public async Task Current_workbook_checks_shared_capacity_before_allowing_player_split()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01097" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var workbook = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample"));
        var players = workbook.Sheet("اللاعبون").Rows;
        var groups = workbook.Sheet("المجموعات").Rows;
        var group = players.Skip(1).GroupBy(row => row[9].Value).First(rows => rows.Count() >= 2);
        var limitedGroups = groups.Select((row, index) => index > 0 && row[0].Value == group.Key
            ? ReplaceCell(row, 5, (group.Count() - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) : row).ToArray();
        var bytes = ImportXlsx.Write(WithRows(workbook, "المجموعات", limitedGroups));
        var preview = await Preview(platform, academy, bytes);
        Assert.False(preview.CanApprove);
        Assert.Contains(preview.Errors, error => error.Column == "كود المجموعة *" && error.Message.Contains("تتجاوز سعتها"));
        Assert.Null(preview.CleanRows);
        using var clean = await DownloadPlayerCorrections(platform, academy, bytes, preview.FileHash, clean: true);
        Assert.Equal(HttpStatusCode.Conflict, clean.StatusCode);
        await AssertNoImportWrites(academy);
    }

    [Fact]
    public async Task Current_workbook_derives_optional_values_and_rejects_a_file_with_no_usable_players()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01097" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var workbook = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample"));
        var players = workbook.Sheet("اللاعبون").Rows;
        var baseline = await Preview(platform, academy, ImportXlsx.Write(workbook));
        Assert.True(baseline.CanApprove);
        var missingOptional = players[1].Select((cell, index) => index is 5 or 6 or 12 or 13 or 14 or 15
            ? new ImportCell("", true) : cell).ToArray();
        var optionalBytes = ImportXlsx.Write(WithRows(workbook, "اللاعبون", [players[0], missingOptional, .. players.Skip(2)]));
        var optional = await Preview(platform, academy, optionalBytes);
        Assert.True(optional.CanApprove, string.Join(" | ", optional.Errors.Select(error => error.Message)));
        var original = baseline.Rows.Single(row => row.LegacyPlayerId == players[1][0].Value);
        var derived = optional.Rows.Single(row => row.LegacyPlayerId == original.LegacyPlayerId);
        Assert.Equal((original.End, original.EntitledSessions, original.RemainingSessions, original.AgreedNet),
            (derived.End, derived.EntitledSessions, derived.RemainingSessions, derived.AgreedNet));

        var invalidPlayers = players.Select((row, index) => index == 0 ? row : ReplaceCell(row, 1, "")).ToArray();
        var invalidBytes = ImportXlsx.Write(WithRows(workbook, "اللاعبون", invalidPlayers));
        var invalid = await Preview(platform, academy, invalidBytes);
        Assert.False(invalid.CanApprove);
        Assert.Null(invalid.CleanRows);
        Assert.Equal(50, invalid.Errors.Count(issue => issue is { Sheet: "اللاعبون", Column: "الاسم الأول *" }));
        await AssertNoImportWrites(academy);
    }

    [Fact]
    public async Task Current_workbook_isolates_invalid_player_cells_and_publishes_only_corrected_records_on_reupload()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01097" + Random.Shared.Next(100000, 999999));
        var path = $"/api/v1/platform/academies/{academy}";
        var entitlements = await platform.GetFromJsonAsync<JsonElement>(path + "/entitlements");
        var features = entitlements.EnumerateArray().ToDictionary(
            feature => feature.GetProperty("feature").GetString()!,
            feature => feature.GetProperty("feature").GetString() is "training" or "matches");
        (await Send(platform, path + "/entitlements", new { planCode = "Trial", features }, true)).EnsureSuccessStatusCode();

        var corrected = await platform.GetByteArrayAsync(path + "/import/connected-sample");
        var workbook = ImportXlsx.Read(corrected);
        var players = workbook.Sheet("اللاعبون").Rows;
        var invalidPhone = players[^2][0].Value;
        var invalidName = players[^1][0].Value;
        var broken = new ImportWorkbook(workbook.Sheets.Select(sheet => sheet.Name != "اللاعبون" ? sheet : sheet with {
            Rows = sheet.Rows.Select((row, index) => index == players.Count - 2
                ? (IReadOnlyList<ImportCell>)row.Select((cell, column) => column == 3 ? new ImportCell("1001234567", false) : cell).ToArray()
                : index == players.Count - 1 ? row.Select((cell, column) => column is 1 or 2 ? new ImportCell("", true) : cell).ToArray() : row).ToArray()
        }).ToArray());
        var brokenBytes = ImportXlsx.Write(broken);
        var preview = await Preview(platform, academy, brokenBytes);
        Assert.False(preview.CanApprove);
        Assert.Equal(new ImportCleanRows(48, 2), preview.CleanRows);
        Assert.Collection(preview.Errors,
            error => Assert.Equal(("اللاعبون", 50, AcademyWorkbookSchema.Players.Single(f => f.Key == "Phone").Header), (error.Sheet, error.Row, error.Column)),
            error => Assert.Equal(("اللاعبون", 51, AcademyWorkbookSchema.Players.Single(f => f.Key == "First").Header), (error.Sheet, error.Row, error.Column)),
            error => Assert.Equal(("اللاعبون", 51, AcademyWorkbookSchema.Players.Single(f => f.Key == "Last").Header), (error.Sheet, error.Row, error.Column)));
        await AssertNoImportWrites(academy);

        var csrf = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(brokenBytes), "file", "mixed.xlsx");
        form.Add(new StringContent(preview.FileHash), "ExpectedFileHash");
        using var download = new HttpRequestMessage(HttpMethod.Post, path + "/import/preview?downloadValidRows=true") { Content = form };
        download.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        using var cleaned = await platform.SendAsync(download);
        cleaned.EnsureSuccessStatusCode();
        Assert.Contains("spreadsheetml", cleaned.Content.Headers.ContentType?.MediaType);
        var cleanBytes = await cleaned.Content.ReadAsByteArrayAsync();
        var clean = await Preview(platform, academy, cleanBytes);
        Assert.True(clean.CanApprove, string.Join("; ", clean.Errors.Select(e => e.Message)));
        Assert.Equal(48, clean.PlayerCount);
        using var stale = await DownloadPlayerCorrections(platform, academy, brokenBytes, new string('0', 64));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var missingHash = await DownloadPlayerCorrections(platform, academy, brokenBytes, null);
        Assert.Equal(HttpStatusCode.Conflict, missingHash.StatusCode);
        using var recovery = await DownloadPlayerCorrections(platform, academy, brokenBytes, preview.FileHash);
        recovery.EnsureSuccessStatusCode();
        Assert.Contains("spreadsheetml", recovery.Content.Headers.ContentType?.MediaType);
        Assert.Contains("academy-player-corrections.xlsx", recovery.Content.Headers.ContentDisposition!.FileName);
        Assert.True(recovery.Headers.CacheControl?.NoStore);
        var correction = ImportXlsx.Read(await recovery.Content.ReadAsByteArrayAsync());
        var rejected = correction.Sheet("اللاعبون").Rows.Skip(1).ToArray();
        Assert.Equal(new[] { invalidPhone, invalidName }, rejected.Select(row => row[0].Value));
        Assert.Equal(new ImportCell("1001234567", false), rejected[0][3]);
        Assert.Equal("", rejected[1][1].Value);
        var guide = correction.Sheet("ابدأ هنا").Rows;
        Assert.Contains(guide, row => row[0].Value == "الصف 2 · الأصل 50" && row[1].Value.Contains("هاتف ولي الأمر"));
        Assert.Contains(guide, row => row[0].Value == "الصف 3 · الأصل 51" && row[1].Value.Contains("الاسم الأول"));
        Assert.Contains(guide, row => row[0].Value == "الصف 3 · الأصل 51" && row[1].Value.Contains("باقي الاسم"));
        Assert.All(correction.Sheets.Where(sheet => sheet.Name == "التسجيلات" || FriendlyImportSchema.Historical(sheet.Name)),
            sheet => Assert.Single(sheet.Rows));
        var fixedPhones = players.TakeLast(2).Select(row => row[3].Value).ToHashSet();
        var guardianRows = workbook.Sheet("أولياء الأمور").Rows.Where((row, index) => index == 0 || fixedPhones.Contains(row[2].Value)).ToArray();
        var correctedRows = ImportXlsx.Write(new ImportWorkbook(correction.Sheets.Select(sheet => sheet.Name switch
        {
            "اللاعبون" => sheet with { Rows = [players[0], .. players.TakeLast(2)] },
            "أولياء الأمور" => sheet with { Rows = guardianRows },
            _ => sheet
        }).ToArray()));
        await AssertNoImportWrites(academy);

        var first = await PublishCorrectionBatch(platform, academy, cleanBytes, clean);
        Assert.Equal(48, first.GetProperty("publishedRows").GetInt32());
        var fixedPreview = await Preview(platform, academy, correctedRows);
        Assert.True(fixedPreview.CanApprove, string.Join("; ", fixedPreview.Errors.Select(e => e.Message)));
        Assert.Equal(2, fixedPreview.PlayerCount);
        var second = await PublishCorrectionBatch(platform, academy, correctedRows, fixedPreview);
        Assert.Equal(2, second.GetProperty("publishedRows").GetInt32());
        Assert.Equal(0, second.GetProperty("replayedRows").GetInt32());
        await using var read = factory.Services.CreateAsyncScope();
        var database = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var saved = await database.Players.IgnoreQueryFilters().Where(p => p.AcademyId == academy).Select(p => p.PlayerCode).ToListAsync();
        Assert.Equal(50, saved.Distinct().Count());
        Assert.Contains(invalidPhone, saved);
        Assert.Contains(invalidName, saved);
        Assert.False(await database.Collections.IgnoreQueryFilters().AnyAsync(c => c.AcademyId == academy));
        Assert.False(await database.Receipts.IgnoreQueryFilters().AnyAsync(r => r.AcademyId == academy));
    }

    private async Task<JsonElement> PublishCorrectionBatch(HttpClient platform, Guid academy, byte[] bytes, ImportPreviewResult approved, bool historyApproved = false)
    {
        using var response = await Stage(platform, academy, bytes, approved, historyApproved: historyApproved);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var job = await db.AcademyImportJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal(ImportJobState.QueuedV3, job.State);
            job.State = ImportJobState.RunningV3;
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(jobId);
        return await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/academies/{academy}/import/jobs/{jobId}");
    }

    private static async Task<HttpResponseMessage> DownloadPlayerCorrections(HttpClient client, Guid academy, byte[] bytes, string? hash, bool clean = false)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "mixed.xlsx");
        if (hash is not null) form.Add(new StringContent(hash), "ExpectedFileHash");
        var action = clean ? "preview?downloadValidRows=true" : "preview/corrections";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform/academies/{academy}/import/{action}") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Current_workbook_recovers_linked_attendance_only_for_the_corrected_player()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01095" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var sample = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample"));
        var attendance = sample.Sheet("حضور اختياري").Rows[1];
        var code = attendance[2].Value[3..];
        var original = sample.Sheet("اللاعبون").Rows.Single(row => row[0].Value == code);
        var bytes = ImportXlsx.Write(BreakPlayerName(sample, code));
        var preview = await Preview(platform, academy, bytes);
        Assert.False(preview.CanApprove);
        Assert.Equal(new ImportCleanRows(49, 1, 1), preview.CleanRows);
        await AssertNoImportWrites(academy);

        using var cleanDownload = await DownloadPlayerCorrections(platform, academy, bytes, preview.FileHash, clean: true);
        using var correctionDownload = await DownloadPlayerCorrections(platform, academy, bytes, preview.FileHash);
        cleanDownload.EnsureSuccessStatusCode();
        correctionDownload.EnsureSuccessStatusCode();
        var cleanBytes = await cleanDownload.Content.ReadAsByteArrayAsync();
        var clean = ImportXlsx.Read(cleanBytes);
        var correction = ImportXlsx.Read(await correctionDownload.Content.ReadAsByteArrayAsync());
        Assert.Equal(4, clean.Sheet("حضور اختياري").Rows.Count);
        Assert.DoesNotContain(clean.Sheet("حضور اختياري").Rows, row => row[2].Value == attendance[2].Value);
        Assert.True(attendance.SequenceEqual(Assert.Single(correction.Sheet("حضور اختياري").Rows.Skip(1))));
        var actualSession = Assert.Single(correction.Sheet("حصص فعلية اختيارية").Rows.Skip(1));
        Assert.Equal(attendance[1].Value, actualSession[0].Value);
        Assert.Contains(clean.Sheet("حصص فعلية اختيارية").Rows, row => row.SequenceEqual(actualSession));
        var approvedClean = await Preview(platform, academy, cleanBytes);
        Assert.True(approvedClean.CanApprove, string.Join("; ", approvedClean.Errors.Select(e => e.Message)));
        Assert.Equal(49, (await PublishCorrectionBatch(platform, academy, cleanBytes, approvedClean)).GetProperty("publishedRows").GetInt32());

        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var sessionsBefore = await db.TrainingSessions.IgnoreQueryFilters().CountAsync(s => s.AcademyId == academy);
        Assert.Equal(3, await db.PlayerAttendances.IgnoreQueryFilters().CountAsync(a => a.AcademyId == academy));
        Assert.False(await db.Players.IgnoreQueryFilters().AnyAsync(p => p.AcademyId == academy && p.PlayerCode == code));

        var repaired = ImportXlsx.Write(WithRows(correction, "اللاعبون", [correction.Sheet("اللاعبون").Rows[0], original]));
        var approvedCorrection = await Preview(platform, academy, repaired);
        Assert.True(approvedCorrection.CanApprove, string.Join("; ", approvedCorrection.Errors.Select(e => e.Message)));
        Assert.Equal(1, approvedCorrection.Historical!.Attendance);
        Assert.Equal(1, (await PublishCorrectionBatch(platform, academy, repaired, approvedCorrection)).GetProperty("publishedRows").GetInt32());
        Assert.Equal(sessionsBefore, await db.TrainingSessions.IgnoreQueryFilters().CountAsync(s => s.AcademyId == academy));
        Assert.Equal(4, await db.PlayerAttendances.IgnoreQueryFilters().CountAsync(a => a.AcademyId == academy));
        Assert.Equal(50, await db.Players.IgnoreQueryFilters().CountAsync(p => p.AcademyId == academy));
        Assert.Equal(50, await db.SportEnrollments.IgnoreQueryFilters().CountAsync(e => e.AcademyId == academy));
        var saved = await (from record in db.PlayerAttendances.IgnoreQueryFilters()
                           join enrollment in db.SportEnrollments.IgnoreQueryFilters() on record.SportEnrollmentId equals enrollment.Id
                           join player in db.Players.IgnoreQueryFilters() on enrollment.PlayerId equals player.Id
                           where record.AcademyId == academy && player.PlayerCode == code
                           select new { record.Id, record.ConsumedSubscriptionPeriodId }).SingleAsync();
        var movement = await db.SubscriptionSessionMovements.IgnoreQueryFilters().SingleAsync(m => m.AcademyId == academy && m.PlayerAttendanceId == saved.Id);
        Assert.Equal(-1, movement.Quantity);
        var period = await db.SubscriptionPeriods.IgnoreQueryFilters().SingleAsync(p => p.Id == saved.ConsumedSubscriptionPeriodId);
        Assert.Equal(period.InitialSessions - 1, period.RemainingSessions);
        Assert.False(await db.Collections.IgnoreQueryFilters().AnyAsync(c => c.AcademyId == academy));
        Assert.False(await db.Receipts.IgnoreQueryFilters().AnyAsync(r => r.AcademyId == academy));
    }

    [Fact]
    public async Task Current_workbook_keeps_a_linked_receipt_and_evaluation_with_the_corrected_player()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01094" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var sample = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample"));
        var attendance = sample.Sheet("حضور اختياري").Rows[1];
        var code = attendance[2].Value[3..];
        var original = sample.Sheet("اللاعبون").Rows.Single(row => row[0].Value == code);
        var session = sample.Sheet("حصص فعلية اختيارية").Rows.Single(row => row[0].Value == attendance[1].Value);
        var group = sample.Sheet("المجموعات").Rows.Single(row => row[0].Value == session[1].Value);
        var coachColumn = Array.FindIndex(AcademyWorkbookSchema.Sheets["المجموعات"], field => field.Key == "Coaches");
        var coach = group[coachColumn].Value.Split(',')[0].Trim();
        var date = session[2].Value;
        var mixed = BreakPlayerName(sample, code);
        void Add(string sheet, params string[] cells) =>
            mixed = WithRows(mixed, sheet, [.. mixed.Sheet(sheet).Rows, ConnectedImportSchema.Row(cells)]);
        Add("معايير تقييم اختيارية", "CR-QA", group[3].Value, "تمرير اختبار", "100", "Passing");
        Add("تقييمات اختيارية", "EV-QA", attendance[2].Value, group[0].Value, coach, date, date[..7], "منشور", session[0].Value, "");
        Add("درجات اختيارية", "SC-QA", "EV-QA", "CR-QA", "75");
        Add("تحصيلات سابقة اختيارية", "PAY-QA", attendance[4].Value, date, original[15].Value, "EGP", "نقدي", "QA-LINKED-RECEIPT");

        var bytes = ImportXlsx.Write(mixed);
        var preview = await Preview(platform, academy, bytes);
        Assert.Equal(new ImportCleanRows(49, 1, 4), preview.CleanRows);
        using var cleanFile = await DownloadPlayerCorrections(platform, academy, bytes, preview.FileHash, clean: true);
        using var correctionFile = await DownloadPlayerCorrections(platform, academy, bytes, preview.FileHash);
        cleanFile.EnsureSuccessStatusCode();
        correctionFile.EnsureSuccessStatusCode();
        var clean = ImportXlsx.Read(await cleanFile.Content.ReadAsByteArrayAsync());
        var correction = ImportXlsx.Read(await correctionFile.Content.ReadAsByteArrayAsync());
        foreach (var sheet in new[] { "تقييمات اختيارية", "درجات اختيارية", "تحصيلات سابقة اختيارية" })
        {
            Assert.Single(clean.Sheet(sheet).Rows);
            Assert.Equal(2, correction.Sheet(sheet).Rows.Count);
        }
        Assert.Equal("CR-QA", correction.Sheet("معايير تقييم اختيارية").Rows[1][0].Value);
        var repaired = ImportXlsx.Write(WithRows(correction, "اللاعبون", [correction.Sheet("اللاعبون").Rows[0], original]));
        var approved = await Preview(platform, academy, repaired);
        Assert.True(approved.CanApprove, string.Join("; ", approved.Errors.Select(e => e.Message)));
        Assert.Equal(1, approved.HistoricalPayments);
        Assert.Equal(1000m, approved.HistoricalPaymentTotal);
        Assert.Equal(1, approved.Historical!.Published);
        using var denied = await Stage(platform, academy, repaired, approved);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);
        await AssertNoImportWrites(academy);
        var result = await PublishCorrectionBatch(platform, academy, repaired, approved, historyApproved: true);
        Assert.Equal(1, result.GetProperty("publishedRows").GetInt32());
        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.Collections.IgnoreQueryFilters().Where(c => c.AcademyId == academy).ToListAsync());
        Assert.Single(await db.Receipts.IgnoreQueryFilters().Where(r => r.AcademyId == academy).ToListAsync());
        Assert.Single(await db.PlayerEvaluations.IgnoreQueryFilters().Where(e => e.AcademyId == academy).ToListAsync());
        Assert.Single(await db.PlayerAttendances.IgnoreQueryFilters().Where(a => a.AcademyId == academy).ToListAsync());

        Add("التسجيلات", "EN-OTHER", code, original[7].Value, original[8].Value, original[9].Value, original[10].Value,
            "PER-OTHER", "2026-10-01", "2026-10-31", "12", "12", "1000", "EGP");
        Add("فترات سابقة اختيارية", "PER-PAST", "EN-OTHER", original[10].Value, "2026-09-01", "2026-09-30", "12", "0", "1000", "EGP");
        var input = FriendlyImportInput.Parse(mixed);
        var split = AcademyWorkbookSchema.SplitInvalidPlayers(mixed, academy, input, input.Errors);
        Assert.True(split.HasValue);
        Assert.Equal(6, split.Value.RelatedRows);
        Assert.Single(split.Value.Clean.Sheet("التسجيلات").Rows);
        Assert.Single(split.Value.Clean.Sheet("فترات سابقة اختيارية").Rows);
        Assert.Equal("EN-OTHER", split.Value.Correction.Sheet("التسجيلات").Rows[1][0].Value);
        Assert.Equal("PER-PAST", split.Value.Correction.Sheet("فترات سابقة اختيارية").Rows[1][0].Value);
    }

    [Fact]
    public async Task Current_workbook_replays_reordered_rows_but_rejects_changed_attendance()
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01093" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        var originalBytes = await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample");
        var original = await Preview(platform, academy, originalBytes);
        Assert.True(original.CanApprove);
        Assert.Equal(50, (await PublishCorrectionBatch(platform, academy, originalBytes, original)).GetProperty("publishedRows").GetInt32());

        var reordered = ImportXlsx.Read(originalBytes);
        foreach (var sheet in new[] { "اللاعبون", "أولياء الأمور", "حضور اختياري" })
        {
            var rows = reordered.Sheet(sheet).Rows;
            reordered = WithRows(reordered, sheet, [rows[0], .. rows.Skip(1).Reverse()]);
        }
        var reorderedBytes = ImportXlsx.Write(reordered);
        var preview = await Preview(platform, academy, reorderedBytes);
        Assert.True(preview.CanApprove, string.Join("; ", preview.Errors.Select(e => e.Message)));
        var replay = await PublishCorrectionBatch(platform, academy, reorderedBytes, preview);
        Assert.Equal(0, replay.GetProperty("publishedRows").GetInt32());
        Assert.Equal(50, replay.GetProperty("replayedRows").GetInt32());

        var attendance = reordered.Sheet("حضور اختياري").Rows;
        var changed = ImportXlsx.Write(WithRows(reordered, "حضور اختياري", [attendance[0], ReplaceCell(attendance[1], 3, "متأخر"), .. attendance.Skip(2)]));
        var changedPreview = await Preview(platform, academy, changed);
        Assert.True(changedPreview.CanApprove, string.Join("; ", changedPreview.Errors.Select(e => e.Message)));
        var rejected = await Assert.ThrowsAsync<ImportRejected>(() => PublishCorrectionBatch(platform, academy, changed, changedPreview));
        Assert.Equal("conflict", rejected.Code);
        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(4, await db.PlayerAttendances.IgnoreQueryFilters().CountAsync(a => a.AcademyId == academy));
        Assert.False(await db.PlayerAttendances.IgnoreQueryFilters().AnyAsync(a => a.AcademyId == academy && a.Status == Academy.Infrastructure.Attendance.AttendanceStatus.Late));
        var failedJob = await db.AcademyImportJobs.SingleAsync(j => j.AcademyId == academy && j.State == ImportJobState.RunningV3);
        failedJob.State = ImportJobState.Failed;
        failedJob.ErrorCode = rejected.Code;
        failedJob.StagedFile = null;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("orphan")]
    [InlineData("session")]
    [InlineData("attendance-code")]
    [InlineData("player-code")]
    [InlineData("period-code")]
    [InlineData("receipt-evidence")]
    public async Task Current_workbook_never_splits_ambiguous_links_or_cross_academy_files(string conflict)
    {
        using var platform = await Platform();
        var academy = await Create(platform, "01095" + Random.Shared.Next(100000, 999999));
        var foreign = await Create(platform, "01096" + Random.Shared.Next(100000, 999999));
        await EnableCorrectionImport(platform, academy);
        await EnableCorrectionImport(platform, foreign);
        var book = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{academy}/import/connected-sample"));
        var history = book.Sheet("حضور اختياري").Rows;
        var player = history[1][2].Value[3..];
        var rowNumber = book.Sheet("اللاعبون").Rows.ToList().FindIndex(row => row[0].Value == player) + 1;
        Assert.True(rowNumber > 1);
        var broken = BreakPlayerName(book, player);
        broken = conflict switch
        {
            "orphan" => WithRows(broken, "حضور اختياري", [.. history, ReplaceCell(ReplaceCell(history[1], 0, "AT-ORPHAN"), 2, "EN-UNKNOWN")]),
            "session" => WithRows(broken, "حصص فعلية اختيارية",
                broken.Sheet("حصص فعلية اختيارية").Rows.Select(row => row[0].Value == history[1][1].Value ? ReplaceCell(row, 1, "GR-UNKNOWN") : row).ToArray()),
            "attendance-code" => WithRows(broken, "حضور اختياري", [history[0], history[1], ReplaceCell(history[2], 0, history[1][0].Value), .. history.Skip(3)]),
            "player-code" => WithRows(broken, "اللاعبون", [.. broken.Sheet("اللاعبون").Rows, book.Sheet("اللاعبون").Rows[rowNumber - 1]]),
            "period-code" => WithRows(broken, "فترات سابقة اختيارية", [broken.Sheet("فترات سابقة اختيارية").Rows[0],
                ConnectedImportSchema.Row(history[1][4].Value, history[2][2].Value, "SP01-S", "2025-01-01", "2025-01-31", "8", "0", "300", "EGP")]),
            "receipt-evidence" => WithRows(broken, "تحصيلات سابقة اختيارية", [broken.Sheet("تحصيلات سابقة اختيارية").Rows[0],
                ConnectedImportSchema.Row("PAY-01", history[1][4].Value, "2026-09-01", "300", "EGP", "نقدي", "QA-RECEIPT"),
                ConnectedImportSchema.Row("PAY-02", history[2][4].Value, "2026-09-01", "300", "EGP", "نقدي", "QA-RECEIPT")]),
            _ => throw new ArgumentOutOfRangeException(nameof(conflict))
        };
        var bytes = ImportXlsx.Write(broken);
        var preview = await Preview(platform, academy, bytes);
        Assert.False(preview.CanApprove);
        Assert.Null(preview.CleanRows);
        Assert.Contains(preview.Errors, error => error.Sheet == "اللاعبون" && error.Row == rowNumber);
        using var rejected = await DownloadPlayerCorrections(platform, academy, bytes, preview.FileHash);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var eligible = ImportXlsx.Write(BreakPlayerName(book, player));
        using var foreignDownload = await DownloadPlayerCorrections(platform, foreign, eligible,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(eligible)));
        Assert.Equal(HttpStatusCode.Conflict, foreignDownload.StatusCode);
        var valid = ImportXlsx.Write(book);
        using var noErrors = await DownloadPlayerCorrections(platform, academy, valid,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(valid)));
        Assert.Equal(HttpStatusCode.Conflict, noErrors.StatusCode);
        await AssertNoImportWrites(academy);
        await AssertNoImportWrites(foreign);
    }

    private static async Task EnableCorrectionImport(HttpClient client, Guid academy)
    {
        var path = $"/api/v1/platform/academies/{academy}/entitlements";
        var entitlements = await client.GetFromJsonAsync<JsonElement>(path);
        var features = entitlements.EnumerateArray().ToDictionary(
            item => item.GetProperty("feature").GetString()!,
            item => item.GetProperty("feature").GetString() is "training" or "matches");
        (await Send(client, path, new { planCode = "Trial", features }, true)).EnsureSuccessStatusCode();
    }

    private static ImportWorkbook BreakPlayerName(ImportWorkbook book, string code) =>
        WithRows(book, "اللاعبون", book.Sheet("اللاعبون").Rows.Select(row => row[0].Value == code ? ReplaceCell(row, 1, "") : row).ToArray());

    private static ImportWorkbook WithRows(ImportWorkbook book, string sheetName, IReadOnlyList<IReadOnlyList<ImportCell>> rows) =>
        new(book.Sheets.Select(sheet => sheet.Name == sheetName ? sheet with { Rows = rows } : sheet).ToArray());

    private static IReadOnlyList<ImportCell> ReplaceCell(IReadOnlyList<ImportCell> row, int column, string value) =>
        row.Select((cell, index) => index == column ? new ImportCell(value, true) : cell).ToArray();
}
