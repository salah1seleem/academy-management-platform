using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Approved_monthly_cash_chart_matches_canonical_month_intersections_and_preserves_totals()
    {
        using var owner = await Staff();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId).Select(b => b.Id).FirstAsync();
        var sport = await db.Sports.Where(s => s.AcademyId == DemoSeed.NogoomAcademyId).Select(s => s.Id).FirstAsync();
        var money = await db.Collections.CountAsync(); var evaluations = await db.PlayerEvaluations.CountAsync();
        foreach (var filter in new[] { "", $"&branchId={branch}&sportId={sport}" })
        {
            const string window = "from=2026-07-12&to=2026-09-28";
            var plain = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/operating-statement?" + window + filter);
            var chart = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/operating-statement?" + window + filter + "&includeMonthly=true");
            Assert.Equal(JsonValueKind.Null, plain.GetProperty("monthly").ValueKind);
            foreach (var key in new[] { "totalIncome", "totalExpenses", "surplus", "refunds", "receivables", "approvedPayrollPayable", "unpaidOperatingExpenses" })
                Assert.Equal(plain.GetProperty(key).GetDecimal(), chart.GetProperty(key).GetDecimal());
            var months = chart.GetProperty("monthly").EnumerateArray().ToArray();
            Assert.Equal(3, months.Length);
            Assert.Equal("2026-07-12", months[0].GetProperty("from").GetString());
            Assert.Equal("2026-07-31", months[0].GetProperty("to").GetString());
            Assert.Equal("2026-09-28", months[2].GetProperty("to").GetString());
            foreach (var month in months)
            {
                var canonical = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/operating-statement?from={month.GetProperty("from").GetString()}&to={month.GetProperty("to").GetString()}{filter}");
                Assert.Equal(canonical.GetProperty("totalIncome").GetDecimal(), month.GetProperty("income").GetDecimal());
                Assert.Equal(canonical.GetProperty("totalExpenses").GetDecimal(), month.GetProperty("expenses").GetDecimal());
                Assert.Equal(canonical.GetProperty("surplus").GetDecimal(), month.GetProperty("surplus").GetDecimal());
            }
            Assert.Equal(chart.GetProperty("totalIncome").GetDecimal(), months.Sum(m => m.GetProperty("income").GetDecimal()));
            Assert.Equal(chart.GetProperty("totalExpenses").GetDecimal(), months.Sum(m => m.GetProperty("expenses").GetDecimal()));
        }
        Assert.Equal(money, await db.Collections.CountAsync()); Assert.Equal(evaluations, await db.PlayerEvaluations.CountAsync());
    }

    [Fact]
    public async Task Approved_monthly_cash_chart_is_bounded_and_rejects_foreign_scope_and_nonowners()
    {
        using var owner = await Staff(); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        using var admin = await Staff(DemoSeed.AdminEmail); using var coach = await Mobile(AcademyRole.Coach); using var guardian = await Mobile(AcademyRole.Guardian);
        const string endpoint = "/api/v1/reports/operating-statement?includeMonthly=true&from=2026-01-01&to=2026-09-28";
        var wide = await owner.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.Equal(3, wide.GetProperty("monthly").GetArrayLength());
        Assert.Equal("2026-07-01", wide.GetProperty("monthly")[0].GetProperty("from").GetString());
        var one = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/operating-statement?includeMonthly=true&from=2026-09-12&to=2026-09-20");
        Assert.Equal(1, one.GetProperty("monthly").GetArrayLength());
        Assert.Equal("2026-09-12", one.GetProperty("monthly")[0].GetProperty("from").GetString());
        Assert.Equal(one.GetProperty("totalIncome").GetDecimal(), one.GetProperty("monthly")[0].GetProperty("income").GetDecimal());
        Assert.Equal(one.GetProperty("totalExpenses").GetDecimal(), one.GetProperty("monthly")[0].GetProperty("expenses").GetDecimal());
        var theirs = await other.GetFromJsonAsync<JsonElement>(endpoint);
        var theirsPlain = await other.GetFromJsonAsync<JsonElement>(endpoint.Replace("includeMonthly=true&", ""));
        Assert.Equal(theirsPlain.GetProperty("totalIncome").GetDecimal(), theirs.GetProperty("totalIncome").GetDecimal());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId).Select(b => b.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(endpoint + $"&branchId={branch}")).StatusCode);
        foreach (var client in new[] { admin, coach, guardian }) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/reports/operating-statement?includeMonthly=true&from=2026-09-20&to=2026-09-12")).StatusCode);
    }
}
