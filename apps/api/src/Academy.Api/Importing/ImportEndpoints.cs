using System.Data;
using System.Security.Cryptography;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public static class ImportEndpoints
{
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
        api.MapPost("/preview", Preview).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import");
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
            var preview = ImportPreview.Validate(book, references, Convert.ToHexString(SHA256.HashData(content)));
            await tx.CommitAsync(ct);
            // No uploads persisted and no database write/OTP/discount application during preview.
            return Results.Ok(preview);
        }
        catch (ImportFileException e) { return Results.Problem(statusCode: 422, title: e.Message); }
        catch (InvalidDataException) { return Results.Problem(statusCode: 413, title: "الملف يتجاوز حدود الرفع أو بنيته غير صالحة."); }
    }
}
