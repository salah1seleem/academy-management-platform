using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Importing;
using Academy.Api.Finance;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    private static IReadOnlyList<ImportCell> ImportCells(params string[] values) => values.Select(v => new ImportCell(v, true)).ToArray();
    private static ImportWorkbook ImportSource(ImportWorkbook book, string source) => new(book.Sheets.Select(s => s.Name == "Metadata" ? s with { Rows = s.Rows.Select(r => r[0].Value == "SourceSystem" ? ImportCells("SourceSystem", source) : r).ToArray() } : s).ToArray());
    private static async Task<HttpResponseMessage> PreviewFile(HttpClient client, byte[] bytes, bool csrf = true)
    {
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "synthetic.xlsx");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/people/import/preview") { Content = form };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    [Fact]
    public async Task IMP_01_template_and_1000_preview_use_same_schema_without_any_operational_or_financial_write()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await db.TrainingGroups.SingleAsync(x => x.Id == f.Group); group.Capacity = 1200; await db.SaveChangesAsync();
        var before = (await db.Players.CountAsync(), await db.GuardianProfiles.CountAsync(), await db.Collections.CountAsync(), await db.ReceivableDocuments.CountAsync(), await db.SubscriptionPeriods.CountAsync());
        var downloaded = await owner.GetByteArrayAsync("/api/v1/people/import/template"); var template = ImportXlsx.Read(downloaded); var book = ImportSource(template, "synthetic-1000");
        Assert.Equal(ImportSchema.Enrollments.Select(c => c.Key), template.Sheet("Enrollments").Rows[0].Select(c => c.Value));
        var rows = new List<IReadOnlyList<ImportCell>> { template.Sheet("Enrollments").Rows[0] };
        for (var i = 0; i < 1000; i++) rows.Add(ImportCells("P" + i, "E" + i, "", "طفل استيراد صناعي " + i, "ولي استيراد صناعي " + i, "010" + i.ToString("00000000"), f.BirthYear + "-01-01", f.A.ToString(), Academy.Api.Slice2.Slice2DemoSeed.SwimmingId.ToString(), "", f.Group.ToString(), plan.ToString(), "2026-09-01", "2026-09-30", "8", "5", "agreed_net", "", "", "540.00", "EGP", "existing", ""));
        book = new(book.Sheets.Select(s => s.Name == "Enrollments" ? s with { Rows = rows } : s).ToArray());
        var response = await PreviewFile(owner, ImportXlsx.Write(book)); response.EnsureSuccessStatusCode(); var preview = await response.Content.ReadFromJsonAsync<ImportPreviewResult>(); Assert.NotNull(preview); Assert.True(preview.CanApprove, string.Join(',', preview.Errors.Select(e => e.Message))); Assert.Equal((1000, 1000, 0), (preview.RowCount, preview.PlayerCount, preview.FinanciallyIncomplete)); Assert.All(preview.Rows, r => Assert.Equal("540.00", r.AgreedNet));
        Assert.Equal(before, (await db.Players.CountAsync(), await db.GuardianProfiles.CountAsync(), await db.Collections.CountAsync(), await db.ReceivableDocuments.CountAsync(), await db.SubscriptionPeriods.CountAsync()));
        await File.WriteAllBytesAsync("/tmp/academy-v2-import-template.xlsx", downloaded); // synthetic references only, independent workbook QA
    }
    [Fact]
    public async Task IMP_06_template_scope_guardian_coach_and_csrf_are_enforced()
    {
        using var owner = await Staff(); var template = await owner.GetByteArrayAsync("/api/v1/people/import/template");
        using var guardian = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/people/import/template")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewFile(guardian, template)).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync("/api/v1/people/import/schema")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PreviewFile(owner, template, false)).StatusCode);
        using var future = await Staff(DemoSeed.FutureOwnerEmail); var response = await PreviewFile(future, template); response.EnsureSuccessStatusCode(); var result = await response.Content.ReadFromJsonAsync<ImportPreviewResult>(); Assert.False(result!.CanApprove); Assert.Contains(result.Errors, e => e.Column == "AcademyCode");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PreviewFile(owner, "not Excel"u8.ToArray())).StatusCode);
    }
    [Fact]
    public async Task IMP_07_template_invalidation_and_branch_restricted_references_are_real()
    {
        var f = await Fixture(false); using var owner = await Staff(); await CreatePlan(owner, f.A);
        var email = $"import.{Guid.NewGuid():N}@example.test";
        var created = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري استيراد صناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار")); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var grants = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "players", m == "players", false, false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.A], grants), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        var template = ImportXlsx.Read(await admin.GetByteArrayAsync("/api/v1/people/import/template"));
        Assert.DoesNotContain(template.Sheet("References").Rows, r => r.Count > 1 && r[1].Value == RenewalOpportunityDemoSeed.BranchId.ToString());
        var allowed = template.Sheet("References").Rows.Where(r => r[0].Value == "branch").ToArray(); Assert.Single(allowed); Assert.Equal(f.A.ToString(), allowed[0][1].Value);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await db.TrainingGroups.SingleAsync(x => x.Id == f.Group); group.Capacity = 4; await db.SaveChangesAsync();
        var preview = await (await PreviewFile(admin, ImportXlsx.Write(template))).Content.ReadFromJsonAsync<ImportPreviewResult>(); Assert.False(preview!.CanApprove); Assert.Contains(preview.Errors, e => e.Column == "ReferenceStamp");
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.A], grants.Select(g => g with { Add = false }).ToArray()), true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewFile(admin, ImportXlsx.Write(template))).StatusCode);
    }
}
