using System.Data;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq.Expressions;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public static class ImportEndpoints
{
    private sealed record PublishedRow(string LegacyPlayerId, string LegacyEnrollmentId, Guid Id, string Player, string PlayerCode, string Sport, string Branch, string GroupName, DateOnly StartDate, DateOnly? EndDate, int? RemainingSessions, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? PriceSnapshot, string CurrencySnapshot);
    // Polling never loads staged workbook bytes or guardian resolutions.
    private sealed record JobSummary(Guid Id, string SourceSystem, ImportJobState State, int RowCount, int PublishedRows, int ReplayedRows, int FinanciallyIncomplete, DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc, int Attempts, string? ErrorCode, string ApprovedBranches);
    private static readonly Expression<Func<AcademyImportJob, JobSummary>> Summary = j => new JobSummary(j.Id, j.SourceSystem, j.State, j.RowCount, j.PublishedRows, j.ReplayedRows, j.FinanciallyIncomplete, j.CreatedAtUtc, j.CompletedAtUtc, j.Attempts, j.ErrorCode, j.ApprovedBranches);
    public static void MapImportPreview(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/people/import").RequireAuthorization(AcademyPermissions.PeopleManage);
        api.MapGet("/schema", () => Results.Ok(new { version = ImportSchema.Version, maxRows = ImportSchema.MaxRows, columns = ImportSchema.Enrollments, balances = ImportSchema.OpenBalances }));
        api.MapGet("/template", async (CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => {
            var t = (await tenant.ResolveAsync())!;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var references = await ImportReferences.Load(db, t.AcademyId);
            var bytes = ImportXlsx.Write(references.Template(clock.Today)); await tx.CommitAsync();
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "academity-import-v2.xlsx");
        });
        api.MapGet("/demo-sample", async (IHostEnvironment env, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => {
            if (!env.IsEnvironment("Demo")) return Results.NotFound();
            var t = (await tenant.ResolveAsync())!; var sample = ImportDemoFile.Create(await ImportReferences.Load(db, t.AcademyId), clock);
            return sample is null ? Results.NotFound() : Results.File(ImportXlsx.Write(sample), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "academity-synthetic-demo.xlsx");
        });
        api.MapPost("/preview", Preview).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import");
        api.MapPost("/commit", Stage).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import");
        api.MapGet("/jobs", async (CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor) => {
            var t = (await tenant.ResolveAsync())!; var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Results.Ok((await db.AcademyImportJobs.AsNoTracking().Where(j => j.AcademyId == t.AcademyId && j.ActorUserId == user).OrderByDescending(j => j.CreatedAtUtc).Take(20).Select(Summary).ToListAsync()).Where(j => Visible(db, j)).Select(JobView));
        });
        api.MapGet("/jobs/{id:guid}", async (Guid id, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor) => {
            var t = (await tenant.ResolveAsync())!; var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var job = await db.AcademyImportJobs.AsNoTracking().Where(j => j.AcademyId == t.AcademyId && j.Id == id && (j.ActorUserId == user || t.Role == AcademyRole.AcademyOwner)).Select(Summary).SingleOrDefaultAsync();
            return job is null || !Visible(db, job) ? Results.NotFound() : Results.Ok(JobView(job));
        });
        api.MapGet("/jobs/{id:guid}/records", async (Guid id, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor) => {
            var t = (await tenant.ResolveAsync())!; var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var job = await db.AcademyImportJobs.AsNoTracking().Where(j => j.AcademyId == t.AcademyId && j.Id == id && (j.ActorUserId == user || t.Role == AcademyRole.AcademyOwner)).Select(Summary).SingleOrDefaultAsync();
            if (job is null || !Visible(db, job)) return Results.NotFound();
            var approvedIds = await db.AcademyImportJobs.Where(j => j.Id == id).Select(j => j.LegacyEnrollmentIds).SingleAsync();
            return Results.Ok(await (from m in db.ImportedEnrollments.AsNoTracking().Where(m => m.JobId == id || m.SourceSystem == job.SourceSystem && approvedIds.Contains(m.LegacyEnrollmentId))
                                     join p in db.Players on m.PlayerId equals p.Id
                                     join e in db.SportEnrollments on m.SportEnrollmentId equals e.Id
                                     join period in db.SubscriptionPeriods on m.SubscriptionPeriodId equals period.Id
                                     orderby m.LegacyEnrollmentId
                                     select new PublishedRow(m.LegacyPlayerId, m.LegacyEnrollmentId, p.Id, p.ArabicName, p.PlayerCode, e.Sport.ArabicName, e.Branch.ArabicName, e.TrainingGroup.ArabicName, period.StartDate, period.EndDate, period.RemainingSessions, period.PriceSnapshot, period.CurrencySnapshot)).ToListAsync());
        });
    }
    private static async Task<IResult> Preview(HttpRequest request, CurrentTenant tenant, FoundationDbContext db, CancellationToken ct)
    {
        if (request.ContentLength > ImportXlsx.MaxFileBytes + 64 * 1024) return Results.Problem(statusCode: 413, title: "الملف أكبر من 5 ميجابايت.");
        if (!request.HasFormContentType) return Results.Problem(statusCode: 422, title: "اختر ملف Excel من زر الاستيراد.");
        try
        {
            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = ImportXlsx.MaxFileBytes, ValueLengthLimit = 512, ValueCountLimit = 8, KeyLengthLimit = 64, MultipartHeadersLengthLimit = 16 * 1024 }, ct);
            if (form.Files.Count != 1 || form.Files["file"] is not { } file || !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return Results.Problem(statusCode: 422, title: "ارفع ملفًا واحدًا بامتداد xlsx من القالب الحالي.");
            if (file.Length is 0 or > ImportXlsx.MaxFileBytes) return Results.Problem(statusCode: 413, title: "حجم الملف غير مسموح.");
            using var bytes = new MemoryStream(); await file.CopyToAsync(bytes, ct); var content = bytes.ToArray();
            var book = ImportXlsx.Read(content); var t = (await tenant.ResolveAsync())!;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var references = await ImportReferences.Load(db, t.AcademyId);
            var preview = ImportPreview.Validate(book, references, Convert.ToHexString(SHA256.HashData(content)), await ImportCommitService.ExistingIds(db, t.AcademyId, book));
            if (preview.CanApprove) preview = preview with { GuardianMatches = await ImportCommitService.Matches(db, t.AcademyId, preview) };
            await tx.CommitAsync(ct);
            // No uploads persisted and no database write/OTP/discount application during preview.
            return Results.Ok(preview);
        }
        catch (ImportFileException e) { return Results.Problem(statusCode: 422, title: e.Message); }
        catch (InvalidDataException) { return Results.Problem(statusCode: 413, title: "الملف يتجاوز حدود الرفع أو بنيته غير صالحة."); }
    }
    private static bool Visible(FoundationDbContext db, AcademyImportJob job) => !db.BranchScopeRestricted || JsonSerializer.Deserialize<Guid[]>(job.ApprovedBranches)!.All(db.RequestBranchIds.Contains);
    private static bool Visible(FoundationDbContext db, JobSummary job) => !db.BranchScopeRestricted || JsonSerializer.Deserialize<Guid[]>(job.ApprovedBranches)!.All(db.RequestBranchIds.Contains);
    private static object JobView(AcademyImportJob j) => new { j.Id, j.SourceSystem, status = j.State.ToString(), j.RowCount, j.PublishedRows, j.ReplayedRows, j.FinanciallyIncomplete, j.CreatedAtUtc, j.CompletedAtUtc, j.Attempts, message = j.ErrorCode is null ? null : new ImportRejected(j.ErrorCode).ArabicMessage };
    private static object JobView(JobSummary j) => new { j.Id, j.SourceSystem, status = j.State.ToString(), j.RowCount, j.PublishedRows, j.ReplayedRows, j.FinanciallyIncomplete, j.CreatedAtUtc, j.CompletedAtUtc, j.Attempts, message = j.ErrorCode is null ? null : new ImportRejected(j.ErrorCode).ArabicMessage };
    private static async Task<IResult> Stage(HttpRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (request.ContentLength > ImportXlsx.MaxFileBytes + 256 * 1024 || !request.HasFormContentType) return Results.Problem(statusCode: 413, title: "اختر ملف القالب ضمن حد الرفع.");
        try
        {
            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = ImportXlsx.MaxFileBytes, ValueLengthLimit = 256 * 1024, ValueCountLimit = 12, KeyLengthLimit = 64 }, ct);
            if (form.Files.Count != 1 || form.Files["file"] is not { } file || !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase) || file.Length is 0 or > ImportXlsx.MaxFileBytes) throw new ImportFileException("اختر ملف xlsx صالحًا بحد أقصى 5 ميجابايت.");
            using var memory = new MemoryStream(); await file.CopyToAsync(memory, ct); var bytes = memory.ToArray(); var book = ImportXlsx.Read(bytes); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var key = request.Headers["Idempotency-Key"].ToString(); if (key.Length is < 8 or > 100 || form["GuardianLinksApproved"] != "true") return Results.Problem(statusCode: 422, title: "اعتمد مراجعة علاقات أولياء الأمور أولًا، مع معرف طلب ثابت.");
            var t = (await tenant.ResolveAsync())!; var actor = Guid.Parse(request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"import-stage:{t.AcademyId}"}, 0))", ct);
            var existing = await db.AcademyImportJobs.SingleOrDefaultAsync(j => j.AcademyId == t.AcademyId && j.ActorUserId == actor && j.IdempotencyKey == key, ct);
            existing ??= await db.AcademyImportJobs.SingleOrDefaultAsync(j => j.AcademyId == t.AcademyId && j.SourceSystem == ImportCommitService.Source(book) && j.FileHash == hash && (j.State == ImportJobState.Queued || j.State == ImportJobState.Running || j.State == ImportJobState.Completed), ct);
            if (existing is not null)
            {
                if (existing.FileHash != hash || !Visible(db, existing) || existing.ActorUserId != actor && t.Role != AcademyRole.AcademyOwner) return Results.Conflict(new { message = "الملف أو معرف الطلب مستخدم بالفعل. راجع نتيجة الاستيراد السابقة." });
                // Compatibility for locally staged jobs predating source-row result tracking.
                // Only identical bytes under the original authority can restore the result keys.
                if (existing.LegacyEnrollmentIds.Length == 0)
                {
                    existing.LegacyEnrollmentIds = book.Sheet("Enrollments").Rows.Skip(1).Where(r => r.Any(c => c.Value.Length > 0)).Select(r => r[1].Value).ToArray();
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                }
                return Results.Ok(JobView(existing));
            }
            if (await db.AcademyImportJobs.CountAsync(j => j.ActorUserId == actor && (j.State == ImportJobState.Queued || j.State == ImportJobState.Running), ct) >= 2) return Results.Problem(statusCode: 429, title: "انتظر اكتمال الدفعات الحالية قبل رفع دفعة أخرى.");
            var preview = ImportPreview.Validate(book, await ImportReferences.Load(db, t.AcademyId), hash, await ImportCommitService.ExistingIds(db, t.AcademyId, book));
            if (!preview.CanApprove || form["ExpectedFileHash"] != hash || form["ExpectedReferenceStamp"] != preview.ReferenceStamp) throw new ImportRejected("stale");
            var balances = ImportCommitService.Balances(book); var balanceApproval = form["OpeningBalancesApproved"] == "true";
            if (balances.Length > 0 && !balanceApproval || preview.FinanciallyIncomplete > 0 && form["AcceptFinancialReview"] != "true") return Results.Problem(statusCode: 422, title: "اعتمد الأرصدة وسندها، وأقر مراجعة البيانات المالية الناقصة إن وجدت.");
            await ImportAuthority.Check(db, t.AcademyId, actor, t.MembershipId, balances.Length > 0);
            var resolutions = JsonSerializer.Deserialize<ImportGuardianResolution[]>(form["GuardianResolutions"].ToString() is { Length: > 0 } json ? json : "[]", new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            var matches = await ImportCommitService.Matches(db, t.AcademyId, preview); var familyKeys = preview.Rows.Select(ImportCommitService.FamilyKey).ToHashSet();
            if (resolutions.Length > 1000 || resolutions.Select(r => r.HouseholdKey).Distinct().Count() != resolutions.Length || resolutions.Any(r => !familyKeys.Contains(r.HouseholdKey) || !matches.Any(m => m.HouseholdKey == r.HouseholdKey && m.Candidates.Contains(r.GuardianId)))) throw new ImportRejected("identity");
            // Existing source household mappings are already explicit decisions; no new selection required on retry.
            var known = await db.ImportedHouseholds.Where(h => h.SourceSystem == preview.SourceSystem).Select(h => h.HouseholdKey).ToArrayAsync(ct);
            if (matches.Any(m => !known.Contains(m.HouseholdKey) && !resolutions.Any(r => r.HouseholdKey == m.HouseholdKey))) throw new ImportRejected("identity");
            var now = time.GetUtcNow(); var job = new AcademyImportJob { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ActorUserId = actor, MembershipId = t.MembershipId, SourceSystem = preview.SourceSystem!, FileHash = hash, ReferenceStamp = preview.ReferenceStamp, IdempotencyKey = key, CutoffDate = preview.CutoffDate!.Value,
                ApprovedBranches = JsonSerializer.Serialize(preview.Rows.Select(r => r.BranchId).Distinct().Order()), GuardianResolutions = JsonSerializer.Serialize(resolutions), LegacyEnrollmentIds = preview.Rows.Select(r => r.LegacyEnrollmentId).ToArray(), GuardianLinksApproved = true, OpeningBalancesApproved = balances.Length > 0 && balanceApproval, StagedFile = bytes, RowCount = preview.RowCount, FinanciallyIncomplete = preview.FinanciallyIncomplete, State = ImportJobState.Queued, CreatedAtUtc = now, UpdatedAtUtc = now, ExpiresAtUtc = now.AddHours(24) };
            db.AcademyImportJobs.Add(job); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Accepted($"/api/v1/people/import/jobs/{job.Id}", JobView(job));
        }
        catch (ImportRejected e) { return Results.Problem(statusCode: e.Code == "authority" ? 403 : 409, title: e.ArabicMessage); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "الملف أو معرف الطلب مرتبط بدفعة قائمة. راجع سجلك أو مسؤول الأكاديمية قبل المحاولة."); }
        catch (ImportFileException e) { return Results.Problem(statusCode: 422, title: e.Message); }
        catch (JsonException) { return Results.Problem(statusCode: 422, title: "راجع اختيار روابط أولياء الأمور قبل الاعتماد."); }
        catch (InvalidDataException) { return Results.Problem(statusCode: 413, title: "الملف يتجاوز حدود الرفع أو بنيته غير صالحة."); }
    }
}
