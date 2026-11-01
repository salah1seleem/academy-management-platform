using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Approved_home_capacity_counts_reconcile_without_loading_followup_child_evidence_and_full_followups_remain_available()
    {
        using var owner=await Staff();using var foreign=await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group=await FollowUpGroup(db);await AbsenceRecords(db,group);
        var money=await db.Collections.CountAsync();var notices=await db.AcademyNotices.CountAsync();
        var response=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-cockpit?branchId={group.BranchId}");
        var capacity=response.GetProperty("capacity");var item=Assert.Single(capacity.GetProperty("items").EnumerateArray(),g=>g.GetProperty("groupId").GetGuid()==group.Id);
        Assert.Equal(20,item.GetProperty("capacity").GetInt32());Assert.Equal(1,item.GetProperty("occupied").GetInt32());Assert.Equal(19,item.GetProperty("available").GetInt32());
        Assert.All(capacity.GetProperty("items").EnumerateArray(),g=>Assert.Equal(group.BranchId,g.GetProperty("branchId").GetGuid()));
        var options=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options");
        var evidence=Assert.Single(options.GetProperty("candidates").EnumerateArray(),c=>c.GetProperty("condition").GetString()=="GroupRecordedAbsences"&&c.GetProperty("source").GetProperty("groupId").GetGuid()==group.Id);
        Assert.Equal(3,evidence.GetProperty("source").GetProperty("recordedAbsences").GetProperty("sessionCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/reports/owner-cockpit?branchId={group.BranchId}")).StatusCode);
        Assert.Equal(money,await db.Collections.CountAsync());Assert.Equal(notices,await db.AcademyNotices.CountAsync());
    }
    [Fact] public async Task Approved_home_trends_reconcile_to_scoped_reports_and_subscription_snapshots_across_local_DST()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Mobile(Academy.Infrastructure.Tenancy.AcademyRole.Guardian);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.IsActive).Select(b=>b.Id).FirstAsync();
        // Oct 31 crosses Cairo's October DST transition; weeks are local dates,
        // not seven fixed 24-hour UTC periods.
        foreach(var selected in new[]{("2026-09-28",""),("2026-10-31",""),("2026-10-31","&branchId="+branch)}) {
            var end=selected.Item1;var filter=selected.Item2;
            var cockpit=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-cockpit?from={end}&to={end}{filter}");
            var weekly=cockpit.GetProperty("collections").GetProperty("weekly").EnumerateArray().ToArray();Assert.Equal(8,weekly.Length);
            var expected=DateOnly.Parse(end).AddDays(-55);
            foreach(var week in weekly) {
                Assert.Equal(expected.ToString("yyyy-MM-dd"),week.GetProperty("from").GetString());
                Assert.Equal(expected.AddDays(6).ToString("yyyy-MM-dd"),week.GetProperty("to").GetString());
                var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?from={week.GetProperty("from").GetString()}&to={week.GetProperty("to").GetString()}{filter}&pageSize=1");
                Assert.Equal(report.GetProperty("netAmount").GetDecimal(),week.GetProperty("amount").GetDecimal());expected=expected.AddDays(7);
            }
            var summary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?from={end}&to={end}{filter}");
            var subscriptions=summary.GetProperty("subscriptions");var months=subscriptions.GetProperty("trend").EnumerateArray().ToArray();Assert.Equal(6,months.Length);
            Assert.Equal(end,months[^1].GetProperty("asOf").GetString());
            Assert.Equal(subscriptions.GetProperty("total").GetInt32(),months[^1].GetProperty("count").GetInt32());
            foreach(var month in months) {
                var snapshot=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/subscription-snapshot?state=subscribed&asOf={month.GetProperty("asOf").GetString()}{filter}");
                Assert.Equal(snapshot.GetProperty("total").GetInt32(),month.GetProperty("count").GetInt32());
            }
        }
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync("/api/v1/reports/owner-cockpit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/reports/owner-cockpit?branchId={branch}")).StatusCode);
        var foreign=await other.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-28&branchId={branch}");
        Assert.All(foreign.GetProperty("subscriptions").GetProperty("trend").EnumerateArray(),p=>Assert.Equal(0,p.GetProperty("count").GetInt32()));
    }
}
