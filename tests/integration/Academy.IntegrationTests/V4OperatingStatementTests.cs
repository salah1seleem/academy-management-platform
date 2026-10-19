using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Tenancy;
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
}
