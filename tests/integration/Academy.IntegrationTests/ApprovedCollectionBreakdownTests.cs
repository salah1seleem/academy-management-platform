using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Approved_collection_sources_and_branches_reconcile_to_real_reports_and_keep_tenant_scope()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);
        var summary=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-08-28&to=2026-09-28");
        Assert.Equal("2026-08-28",summary.GetProperty("collectionMonthFrom").GetString());
        var collections=summary.GetProperty("collections");
        Assert.Equal(collections.GetProperty("total").GetDecimal(),collections.GetProperty("month").GetDecimal());
        var defaults=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary");
        Assert.Equal(DateOnly.Parse(defaults.GetProperty("localToday").GetString()!).AddMonths(-1).ToString("yyyy-MM-dd"),defaults.GetProperty("collectionMonthFrom").GetString());
        foreach(var daily in new[]{true,false}) {
            var key=daily?"today":"month";
            var from=daily?summary.GetProperty("collectionDay").GetString():summary.GetProperty("collectionMonthFrom").GetString();
            var dates=$"from={from}&to={summary.GetProperty("collectionDay").GetString()}";
            var sources=collections.GetProperty(key+"Sources").EnumerateArray().ToArray();
            var branches=collections.GetProperty(key+"Branches").EnumerateArray().ToArray();
            Assert.Equal(collections.GetProperty(key).GetDecimal(),sources.Sum(s=>s.GetProperty("amount").GetDecimal()));
            Assert.Equal(collections.GetProperty(key).GetDecimal(),branches.Sum(s=>s.GetProperty("amount").GetDecimal()));
            var all=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+dates);
            Assert.Equal(collections.GetProperty(key).GetDecimal(),all.GetProperty("netAmount").GetDecimal());
            Assert.Equal(all.GetProperty("netAmount").GetDecimal(), all.GetProperty("sourceSummary").EnumerateArray().Sum(s => s.GetProperty("amount").GetDecimal()));
            var paged = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+dates+"&page=1&pageSize=1");
            Assert.True(paged.GetProperty("items").GetArrayLength() <= 1);
            Assert.Equal(all.GetProperty("totalCount").GetInt32(), paged.GetProperty("totalCount").GetInt32());
            Assert.Equal(all.GetProperty("sourceSummary").GetRawText(), paged.GetProperty("sourceSummary").GetRawText());
            foreach(var source in sources) {
                var kind=source.GetProperty("collectionType").GetString();
                var q=dates+"&collectionType="+kind;
                if(source.GetProperty("subscriptionKind").ValueKind==JsonValueKind.String)q+="&subscriptionKind="+source.GetProperty("subscriptionKind").GetString();
                var report=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+q);
                Assert.Equal(source.GetProperty("amount").GetDecimal(),kind=="Refund"?report.GetProperty("netAmount").GetDecimal():report.GetProperty("totalAmount").GetDecimal());
                Assert.Equal(report.GetProperty("netAmount").GetDecimal(), report.GetProperty("sourceSummary").EnumerateArray().Sum(s => s.GetProperty("amount").GetDecimal()));
                var csv=await owner.GetAsync("/api/v1/reports/financial/export?"+q);csv.EnsureSuccessStatusCode();
                Assert.Equal("text/csv",csv.Content.Headers.ContentType!.MediaType);
            }
            foreach(var branch in branches.Where(b=>b.GetProperty("id").ValueKind==JsonValueKind.String)) {
                var q=dates+"&branchId="+branch.GetProperty("id").GetString();
                var report=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+q);
                Assert.Equal(branch.GetProperty("amount").GetDecimal(),report.GetProperty("netAmount").GetDecimal());
                Assert.Equal(0m,(await other.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+q)).GetProperty("netAmount").GetDecimal());
            }
        }
        foreach(var q in new[]{"collectionType=Product&subscriptionKind=Enrollment","collectionType=Subscription&subscriptionKind=Invalid"})
            Assert.Equal(HttpStatusCode.BadRequest,(await owner.GetAsync("/api/v1/reports/financial?allDates=true&"+q)).StatusCode);
    }
}
