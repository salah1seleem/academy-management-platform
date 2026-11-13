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
    private sealed record JobSummary(Guid Id, string SourceSystem, ImportJobState State, int RowCount, int PublishedRows, int ReplayedRows, int FinanciallyIncomplete, DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc, int Attempts, string? ErrorCode, string ApprovedBranches, string? ResultSummary);
    private static readonly Expression<Func<AcademyImportJob, JobSummary>> Summary = j => new JobSummary(j.Id, j.SourceSystem, j.State, j.RowCount, j.PublishedRows, j.ReplayedRows, j.FinanciallyIncomplete, j.CreatedAtUtc, j.CompletedAtUtc, j.Attempts, j.ErrorCode, j.ApprovedBranches, j.ResultSummary);
    public static void MapImportPreview(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/people/import").RequireAuthorization(AcademyPermissions.PeopleManage);
        MapRoutes(api);
        MapRoutes(app.MapGroup("/api/v1/platform/academies/{academyId:guid}/import").RequireAuthorization(Academy.Api.Platform.PlatformIdentity.Policy));
    }
    private static DateOnly AcademyDay(ImportScope scope, ISubscriptionClock clock) => clock.DateAt(clock.UtcNow, scope.TimeZone);

    private static void MapRoutes(RouteGroupBuilder api)
    {
        api.AddEndpointFilter(async (context,next) => {
            try { return await next(context); }
            catch (ImportRejected e) { return Results.Problem(statusCode:e.Code=="authority"?403:409,title:e.ArabicMessage); }
        });
        api.MapGet("/schema", (IHostEnvironment env) => Results.Ok(new { rehearsalAvailable = env.IsEnvironment("Demo"), version = ImportSchema.Version, connectedVersion = AcademyWorkbookSchema.Version, maxRows = ImportSchema.MaxRows, maxFileBytes = ImportXlsx.MaxFileBytes, columns = ImportSchema.Enrollments, balances = ImportSchema.OpenBalances,
            connectedSheets = new[] { new { name = "ابدأ هنا", optional = true, columns = Array.Empty<ConnectedImportSchema.Field>() } }.Concat(AcademyWorkbookSchema.Sheets.Select(s => new { name = s.Key, optional = FriendlyImportSchema.Historical(s.Key) || new[] { "مستويات التدريب", "العروض", "البطولات", "الفرق", "الأرصدة الافتتاحية" }.Contains(s.Key), columns = s.Value })) }));
        api.MapGet("/connected-template", async (ImportRequestContext tenant, FoundationDbContext db, ISubscriptionClock clock) => {
            var t = (await tenant.ResolveAsync())!;
            await tenant.CheckConnected(t);
            var currency = await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.DefaultCurrency).SingleAsync();
            return Results.File(ImportXlsx.Write(AcademyWorkbookSchema.Template(t.AcademyId,currency,AcademyDay(t, clock))),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","i-academy-one-blank.xlsx");
        });
        api.MapGet("/connected-sample", async (ImportRequestContext tenant, FoundationDbContext db, ISubscriptionClock clock, IConfiguration configuration, IWebHostEnvironment env) => {
            var t = (await tenant.ResolveAsync())!;
            await tenant.CheckConnected(t);
            var samplePath = configuration["Imports:CompletedSamplePath"] ?? Path.Combine(env.ContentRootPath,"Assets","academy-completed-sample.xlsx");
            var book = AcademyWorkbookSchema.BindSample(ImportXlsx.Read(await File.ReadAllBytesAsync(samplePath)),t.AcademyId, rebasePhones: configuration["Imports:CompletedSamplePath"] is null);
            return Results.File(ImportXlsx.Write(book),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","i-academy-one-completed-50.xlsx");
        });
        api.MapGet("/rehearsal-sample", async (IHostEnvironment env, ImportRequestContext tenant, FoundationDbContext db, ISubscriptionClock clock) => {
            if (!env.IsEnvironment("Demo")) return Results.NotFound();
            var t = (await tenant.ResolveAsync())!; await tenant.CheckConnected(t);
            return Results.File(ImportXlsx.Write(FriendlyImportRehearsal.Create(t.AcademyId, AcademyDay(t, clock), phoneOffset: 20_000_000 + (int)(BitConverter.ToUInt32(t.AcademyId.ToByteArray(), 0) % 7_000_000))), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "professionals-300-fictional-history.xlsx");
        });
        api.MapPost("/rehearsal-swimming",async (DemoSwimmingWrite body,IHostEnvironment env,ImportRequestContext tenant,ISubscriptionClock clock)=>{
            if(!env.IsEnvironment("Demo"))return Results.NotFound();
            var name=body.GuardianName?.Trim()??"";var phone=string.IsNullOrWhiteSpace(body.GuardianPhone)?null:Academy.Api.Auth.EgyptPhoneNormalizer.Normalize(body.GuardianPhone);
            if(name.Length>160||(name.Length>0)!=(phone!=null)||!string.IsNullOrWhiteSpace(body.GuardianPhone)&&phone==null)return Results.UnprocessableEntity(new{title="لربط شخص قائم، أدخل اسمه وهاتفه الصحيحين معًا، أو اتركهما فارغين للنموذج الخيالي."});
            var t=(await tenant.ResolveAsync())!;await tenant.CheckConnected(t);
            return Results.File(ImportXlsx.Write(FriendlyImportRehearsal.Swimming(t.AcademyId,AcademyDay(t, clock),name,phone)),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","swimming-10-fictional-history.xlsx");
        }).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/rehearsal-correction", async (string? variant, IHostEnvironment env, ImportRequestContext tenant, ISubscriptionClock clock) => {
            if (!env.IsEnvironment("Demo")) return Results.NotFound();
            if (variant is not (null or "corrected" or "identity" or "links" or "history")) return Results.UnprocessableEntity(new { message = "اختر نموذج التصحيح المعروف." });
            var t = (await tenant.ResolveAsync())!; await tenant.CheckConnected(t);
            return Results.File(ImportXlsx.Write(FriendlyImportRehearsal.Correction(t.AcademyId, AcademyDay(t, clock), variant ?? "corrected")), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "small-" + (variant ?? "corrected") + ".xlsx");
        });
        api.MapGet("/template", async (ImportRequestContext tenant, FoundationDbContext db, ISubscriptionClock clock, TimeProvider time) => {
            var t = (await tenant.ResolveAsync())!;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var references = await ImportReferences.Load(db, t.AcademyId, time);
            var bytes = ImportXlsx.Write(references.Template(AcademyDay(t, clock))); await tx.CommitAsync();
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "academity-import-v2.xlsx");
        });
        api.MapGet("/demo-sample", async (IHostEnvironment env, ImportRequestContext tenant, FoundationDbContext db, ISubscriptionClock clock, TimeProvider time) => {
            if (!env.IsEnvironment("Demo")) return Results.NotFound();
            var t = (await tenant.ResolveAsync())!; var sample = ImportDemoFile.Create(await ImportReferences.Load(db, t.AcademyId, time), AcademyDay(t, clock));
            return sample is null ? Results.NotFound() : Results.File(ImportXlsx.Write(sample), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "academity-synthetic-demo.xlsx");
        });
        api.MapPost("/preview", Preview).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import");
        api.MapPost("/preview/corrections", Preview).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import-correction");
        api.MapPost("/commit", Stage).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import");
        api.MapGet("/jobs", async (ImportRequestContext tenant, FoundationDbContext db, ClaimsPrincipal actor) => {
            var t = (await tenant.ResolveAsync())!; var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Results.Ok((await db.AcademyImportJobs.AsNoTracking().Where(j => j.AcademyId == t.AcademyId && (j.ActorUserId == user || t.Platform || t.Role == AcademyRole.AcademyOwner)).OrderByDescending(j => j.CreatedAtUtc).Take(20).Select(Summary).ToListAsync()).Where(j => Visible(db, j)).Select(JobView));
        });
        api.MapGet("/jobs/by-request", async (string? requestKey, ImportRequestContext tenant, FoundationDbContext db, ClaimsPrincipal actor, HttpResponse response, CancellationToken ct) => {
            response.Headers.CacheControl = "no-store";
            var t = (await tenant.ResolveAsync())!;
            if (requestKey is not { Length: >= 8 and <= 100 }) return Results.NotFound();
            var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var job = await db.AcademyImportJobs.AsNoTracking()
                .Where(j => j.AcademyId == t.AcademyId && j.ActorUserId == user && j.IdempotencyKey == requestKey)
                .Select(Summary).SingleOrDefaultAsync(ct);
            return job is null || !Visible(db, job) ? Results.NotFound() : Results.Ok(JobView(job));
        });
        api.MapGet("/jobs/{id:guid}", async (Guid id, ImportRequestContext tenant, FoundationDbContext db, ClaimsPrincipal actor) => {
            var t = (await tenant.ResolveAsync())!; var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var job = await db.AcademyImportJobs.AsNoTracking().Where(j => j.AcademyId == t.AcademyId && j.Id == id && (j.ActorUserId == user || t.Platform || t.Role == AcademyRole.AcademyOwner)).Select(Summary).SingleOrDefaultAsync();
            return job is null || !Visible(db, job) ? Results.NotFound() : Results.Ok(JobView(job));
        });
        api.MapGet("/jobs/{id:guid}/records", async (Guid id, ImportRequestContext tenant, FoundationDbContext db, ClaimsPrincipal actor) => {
            var t = (await tenant.ResolveAsync())!; var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var job = await db.AcademyImportJobs.AsNoTracking().Where(j => j.AcademyId == t.AcademyId && j.Id == id && (j.ActorUserId == user || t.Platform || t.Role == AcademyRole.AcademyOwner)).Select(Summary).SingleOrDefaultAsync();
            if (job is null || !Visible(db, job)) return Results.NotFound();
            var approvedIds = await db.AcademyImportJobs.Where(j => j.Id == id).Select(j => j.LegacyEnrollmentIds).SingleAsync();
            return Results.Ok(await (from m in db.ImportedEnrollments.AsNoTracking().Where(m => m.JobId == id || m.SourceSystem == job.SourceSystem && approvedIds.Contains(m.LegacyEnrollmentId))
                                     join p in db.Players on m.PlayerId equals p.Id
                                     join e in db.SportEnrollments on m.SportEnrollmentId equals e.Id
                                     join period in db.SubscriptionPeriods on m.SubscriptionPeriodId equals period.Id
                                     orderby m.LegacyEnrollmentId
                                     select new PublishedRow(m.LegacyPlayerId, m.LegacyEnrollmentId, p.Id, p.ArabicName, p.PlayerCode, e.Sport.ArabicName, e.Branch.ArabicName, e.TrainingGroup == null ? "في انتظار المجموعة" : e.TrainingGroup.ArabicName, period.StartDate, period.EndDate, period.RemainingSessions, period.PriceSnapshot, period.CurrencySnapshot)).ToListAsync());
        });
    }
    private static async Task<IResult> Preview(HttpRequest request, ImportRequestContext tenant, FoundationDbContext db, TimeProvider time, CancellationToken ct)
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
            var hash = Convert.ToHexString(SHA256.HashData(content));
            var downloadCorrections = request.Path.Value?.TrimEnd('/').EndsWith("/preview/corrections", StringComparison.OrdinalIgnoreCase) == true;
            var downloadValidRows = request.Query["downloadValidRows"] == "true";
            var download = downloadCorrections || downloadValidRows;
            if (download && form["ExpectedFileHash"] != hash)
                return Results.Conflict(new { title = "تغيّر الملف منذ المعاينة. افحص الملف من جديد." });
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            ImportPreviewResult preview;
            ImportReferences references;
            if (ConnectedImportSchema.IsConnected(book))
            {
                await tenant.CheckConnected(t);
                var connected = await ConnectedImportPlan.Build(book, db, t.AcademyId, time);
                references = connected.References;
                preview = connected.Validate(hash, await ImportCommitService.ExistingIds(db, t.AcademyId, book));
                if (preview.CanApprove)
                    preview = preview with { GuardianMatches = await ImportCommitService.Matches(db, t.AcademyId, preview, connected.History?.ReviewedFamilies) };
                else if (connected.Friendly is { } friendly &&
                    AcademyWorkbookSchema.SplitInvalidPlayers(book, t.AcademyId, friendly, preview.Errors) is { } split)
                {
                    var clean = split.Clean;
                    var cleanPlan = await ConnectedImportPlan.Build(clean, db, t.AcademyId, time);
                    var cleanPreview = cleanPlan.Validate("", await ImportCommitService.ExistingIds(db, t.AcademyId, clean));
                    if (cleanPreview.CanApprove && cleanPreview.PlayerCount > 0)
                    {
                        if (download)
                        {
                            await tx.CommitAsync(ct);
                            request.HttpContext.Response.Headers.CacheControl = "private, no-store";
                            return Results.File(ImportXlsx.Write(downloadCorrections ? split.Correction : clean),
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                downloadCorrections ? "academy-player-corrections.xlsx" : "academy-valid-players.xlsx");
                        }
                        var excludedRows = split.Correction.Sheet("اللاعبون").Rows.Count - 1;
                        preview = preview with { CleanRows = new(cleanPreview.PlayerCount, excludedRows, split.RelatedRows) };
                    }
                }
            }
            else
            {
                references = await ImportReferences.Load(db, t.AcademyId, time);
                preview = ImportPreview.Validate(book, references, hash, await ImportCommitService.ExistingIds(db, t.AcademyId, book));
                if (preview.CanApprove) preview = preview with { GuardianMatches = await ImportCommitService.Matches(db, t.AcademyId, preview) };
            }
            await tx.CommitAsync(ct);
            if (download) return Results.Conflict(new { title = "تعذّر فصل الصفوف بأمان. صحّح المشكلات في الملف الأصلي وأعد فحصه." });
            // No uploads persisted and no database write/OTP/discount application during preview.
            var names = references.Values.ToLookup(reference => (reference.Kind, reference.Id), reference => reference.Name);
            string Name(string kind, Guid id) => names[(kind, id)].FirstOrDefault() ?? "";
            return Results.Ok(preview with { RegistrationLabels = preview.Rows.ToDictionary(row => row.LegacyEnrollmentId,
                row => new ImportRegistrationLabel(Name("sport", row.SportId), Name("group", row.GroupId), Name("branch", row.BranchId))) });
        }
        catch (ImportRejected e) { return Results.Problem(statusCode: e.Code == "authority" ? 403 : 409, title: e.ArabicMessage); }
        catch (ImportFileException e) { return Results.Problem(statusCode: 422, title: e.Message); }
        catch (InvalidDataException) { return Results.Problem(statusCode: 413, title: "الملف يتجاوز حدود الرفع أو بنيته غير صالحة."); }
    }
    private static bool Visible(FoundationDbContext db, AcademyImportJob job) => !db.BranchScopeRestricted || JsonSerializer.Deserialize<Guid[]>(job.ApprovedBranches)!.All(db.RequestBranchIds.Contains);
    private static bool Visible(FoundationDbContext db, JobSummary job) => !db.BranchScopeRestricted || JsonSerializer.Deserialize<Guid[]>(job.ApprovedBranches)!.All(db.RequestBranchIds.Contains);
    private static object JobView(AcademyImportJob j, bool replayedRequest = false) => new { replayedRequest, j.Id, j.SourceSystem, status = j.State == ImportJobState.QueuedV3 ? "Queued" : j.State == ImportJobState.RunningV3 ? "Running" : j.State.ToString(), j.RowCount, j.PublishedRows, j.ReplayedRows, j.FinanciallyIncomplete, j.CreatedAtUtc, j.CompletedAtUtc, j.Attempts, result = j.ResultSummary is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(j.ResultSummary), message = j.ErrorCode is null ? null : new ImportRejected(j.ErrorCode).ArabicMessage };
    private static object JobView(JobSummary j) => new { j.Id, j.SourceSystem, status = j.State == ImportJobState.QueuedV3 ? "Queued" : j.State == ImportJobState.RunningV3 ? "Running" : j.State.ToString(), j.RowCount, j.PublishedRows, j.ReplayedRows, j.FinanciallyIncomplete, j.CreatedAtUtc, j.CompletedAtUtc, j.Attempts, result = j.ResultSummary is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(j.ResultSummary), message = j.ErrorCode is null ? null : new ImportRejected(j.ErrorCode).ArabicMessage };
    private static async Task<IResult> Stage(HttpRequest request, ImportRequestContext tenant, FoundationDbContext db, TimeProvider time, CancellationToken ct)
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
            existing ??= await db.AcademyImportJobs.SingleOrDefaultAsync(j => j.AcademyId == t.AcademyId && j.SourceSystem == ImportCommitService.Source(book) && j.FileHash == hash && (j.State == ImportJobState.Queued || j.State == ImportJobState.Running || j.State == ImportJobState.QueuedV3 || j.State == ImportJobState.RunningV3 || j.State == ImportJobState.Completed), ct);
            if (existing is not null)
            {
                if (existing.FileHash != hash || !Visible(db, existing) || existing.ActorUserId != actor && !t.Platform && t.Role != AcademyRole.AcademyOwner) return Results.Conflict(new { message = "الملف أو معرف الطلب مستخدم بالفعل. راجع نتيجة الاستيراد السابقة." });
                // Compatibility for locally staged jobs predating source-row result tracking.
                // Only identical bytes under the original authority can restore the result keys.
                if (existing.LegacyEnrollmentIds.Length == 0)
                {
                    existing.LegacyEnrollmentIds = book.Sheet(ConnectedImportSchema.IsConnected(book) ? "الأعضاء" : "Enrollments").Rows.Skip(1).Where(r => r.Any(c => c.Value.Length > 0)).Select(r => r[1].Value).ToArray();
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                }
                return Results.Ok(JobView(existing, true));
            }
            if (await db.AcademyImportJobs.CountAsync(j => j.ActorUserId == actor && (j.State == ImportJobState.Queued || j.State == ImportJobState.Running || j.State == ImportJobState.QueuedV3 || j.State == ImportJobState.RunningV3), ct) >= 2) return Results.Problem(statusCode: 429, title: "انتظر اكتمال الدفعات الحالية قبل رفع دفعة أخرى.");
            ConnectedImportPlan? connected = null;
            if (ConnectedImportSchema.IsConnected(book)) { await tenant.CheckConnected(t); connected = await ConnectedImportPlan.Build(book,db,t.AcademyId,time); }
            var preview = connected?.Validate(hash, await ImportCommitService.ExistingIds(db,t.AcademyId,book)) ?? ImportPreview.Validate(book, await ImportReferences.Load(db, t.AcademyId, time), hash, await ImportCommitService.ExistingIds(db, t.AcademyId, book));
            if (!preview.CanApprove || form["ExpectedFileHash"] != hash || form["ExpectedReferenceStamp"] != preview.ReferenceStamp) throw new ImportRejected("stale");
            var balances = ImportCommitService.Balances(connected?.Legacy ?? book); var balanceApproval = form["OpeningBalancesApproved"] == "true";
            if (balances.Length > 0 && !balanceApproval || preview.FinanciallyIncomplete > 0 && form["AcceptFinancialReview"] != "true") return Results.Problem(statusCode: 422, title: "اعتمد الأرصدة وسندها، وأقر مراجعة البيانات المالية الناقصة إن وجدت.");
            var historyPayments = connected?.History?.PaymentsCount > 0;
            var paymentsApproved = form["HistoricalPaymentsApproved"] == "true";
            if (historyPayments && !paymentsApproved) return Results.Problem(statusCode: 422, title: "راجع عدد التحصيلات السابقة ومجموعها وسند كل قبض، ثم اعتمد التاريخ المالي صراحةً.");
            await tenant.Check(t, actor, balances.Length > 0 || historyPayments);
            var resolutions = JsonSerializer.Deserialize<ImportGuardianResolution[]>(form["GuardianResolutions"].ToString() is { Length: > 0 } json ? json : "[]", new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            var matches = await ImportCommitService.Matches(db, t.AcademyId, preview, connected?.History?.ReviewedFamilies); var familyKeys = preview.Rows.Select(ImportCommitService.FamilyKey).ToHashSet();
            if (resolutions.Length > 1000 || resolutions.Select(r => r.HouseholdKey).Distinct().Count() != resolutions.Length || resolutions.Any(r => !familyKeys.Contains(r.HouseholdKey) || !matches.Any(m => m.HouseholdKey == r.HouseholdKey && m.Candidates.Contains(r.GuardianId)))) throw new ImportRejected("identity");
            // Existing source household mappings are already explicit decisions; no new selection required on retry.
            var known = await db.ImportedHouseholds.Where(h => h.SourceSystem == preview.SourceSystem).Select(h => h.HouseholdKey).ToArrayAsync(ct);
            if (matches.Any(m => !known.Contains(m.HouseholdKey) && !resolutions.Any(r => r.HouseholdKey == m.HouseholdKey))) throw new ImportRejected("identity");
            var now = time.GetUtcNow(); var job = new AcademyImportJob { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ActorUserId = actor, MembershipId = t.MembershipId, PlatformInitiated = t.Platform, SourceSystem = preview.SourceSystem!, FileHash = hash, ReferenceStamp = preview.ReferenceStamp, IdempotencyKey = key, CutoffDate = preview.CutoffDate!.Value,
                ApprovedBranches = JsonSerializer.Serialize(preview.Rows.Select(r => r.BranchId).Distinct().Order()), GuardianResolutions = JsonSerializer.Serialize(resolutions), LegacyEnrollmentIds = preview.Rows.Select(r => r.LegacyEnrollmentId).ToArray(), GuardianLinksApproved = true, OpeningBalancesApproved = balances.Length > 0 && balanceApproval || historyPayments && paymentsApproved, StagedFile = bytes, RowCount = preview.RowCount, FinanciallyIncomplete = preview.FinanciallyIncomplete, State = AcademyWorkbookSchema.IsCurrent(book) ? ImportJobState.QueuedV3 : ImportJobState.Queued, CreatedAtUtc = now, UpdatedAtUtc = now, ExpiresAtUtc = now.AddHours(24) };
            db.AcademyImportJobs.Add(job);
            if (t.Platform) db.PlatformAudits.Add(new() { Id = Guid.NewGuid(), ActorUserId = actor, TargetAcademyId = t.AcademyId, Action = "academy.import.stage", Details = JsonSerializer.Serialize(new { job.Id, job.FileHash, job.RowCount, job.SourceSystem, accountLinksApproved = connected?.AccountLinks.Count ?? 0 }), AtUtc = now });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Accepted($"{request.Path.Value![..request.Path.Value!.LastIndexOf('/')]}/jobs/{job.Id}", JobView(job));
        }
        catch (ImportRejected e)
        {
            return Results.Problem(statusCode: e.Code == "authority" ? 403 : 409,
                title: e.ArabicMessage, extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "الملف أو معرف الطلب مرتبط بدفعة قائمة. راجع سجلك أو مسؤول الأكاديمية قبل المحاولة."); }
        catch (ImportFileException e) { return Results.Problem(statusCode: 422, title: e.Message); }
        catch (JsonException) { return Results.Problem(statusCode: 422, title: "راجع اختيار روابط أولياء الأمور قبل الاعتماد."); }
        catch (InvalidDataException) { return Results.Problem(statusCode: 413, title: "الملف يتجاوز حدود الرفع أو بنيته غير صالحة."); }
    }
}

public sealed record DemoSwimmingWrite(string? GuardianName,string? GuardianPhone);
