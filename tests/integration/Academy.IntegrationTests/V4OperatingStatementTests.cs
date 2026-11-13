using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task V4_operating_cash_statement_reconciles_with_receipts_without_claiming_earned_revenue()
    {
        using var owner=await Staff();using var coach=await Mobile(AcademyRole.Coach);using var admin=await Staff(DemoSeed.AdminEmail);
        const string range="from=2026-09-01&to=2026-09-28";
        var d=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/operating-statement?"+range);
        var f=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+range);
        Assert.Equal("Cash",d.GetProperty("basis").GetString());Assert.Equal(JsonValueKind.Null,d.GetProperty("earnedRevenue").ValueKind);
        var income=d.GetProperty("income").EnumerateArray().Sum(x=>x.GetProperty("amount").GetDecimal());var expense=d.GetProperty("expenses").EnumerateArray().Sum(x=>x.GetProperty("amount").GetDecimal());
        Assert.Equal(f.GetProperty("netAmount").GetDecimal(),income);Assert.Equal(income,d.GetProperty("totalIncome").GetDecimal());Assert.Equal(expense,d.GetProperty("totalExpenses").GetDecimal());Assert.Equal(income-expense,d.GetProperty("surplus").GetDecimal());
        Assert.Equal(f.GetProperty("totalRefunds").GetDecimal(),d.GetProperty("refunds").GetDecimal());Assert.NotEmpty(d.GetProperty("warnings").EnumerateArray());
        foreach(var c in new[]{coach,admin})Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/reports/operating-statement?"+range)).StatusCode);
        var export=await owner.GetAsync("/api/v1/reports/operating-statement?"+range+"&format=csv");Assert.Equal(HttpStatusCode.OK,export.StatusCode);Assert.Contains("أساس النقدي",await export.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Dashboard_history_keeps_selected_cash_scope_and_compares_equal_adjacent_dates()
    {
        using var owner = await Staff();
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId)
            .OrderBy(b => b.Id).Select(b => b.Id).FirstAsync();
        const string endpoint = "/api/v1/reports/operating-statement";
        var report = await owner.GetFromJsonAsync<JsonElement>(
            $"{endpoint}?from=2026-09-01&to=2026-09-28&branchId={branch}&includeHistory=true");
        var previous = await owner.GetFromJsonAsync<JsonElement>(
            $"{endpoint}?from=2026-08-04&to=2026-08-31&branchId={branch}");
        var previousSurplus = previous.GetProperty("surplus").GetDecimal();
        var currentSurplus = report.GetProperty("surplus").GetDecimal();
        Assert.Equal(previousSurplus, report.GetProperty("previousSurplus").GetDecimal());
        var percentage = report.GetProperty("surplusChangePercentage");
        if (previousSurplus == 0) Assert.Equal(JsonValueKind.Null, percentage.ValueKind);
        else Assert.Equal(Math.Round((currentSurplus - previousSurplus) * 100 / Math.Abs(previousSurplus), 1), percentage.GetDecimal());
        Assert.Equal(new[] {"2026-07-01", "2026-08-01", "2026-09-01"},
            report.GetProperty("monthly").EnumerateArray().Select(m => m.GetProperty("from").GetString()));
        Assert.Equal("2026-09-28", report.GetProperty("to").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(
            $"{endpoint}?from=2026-09-01&to=2026-09-28&branchId={branch}&includeHistory=true")).StatusCode);
    }

}
