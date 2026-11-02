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
    // Written for the single final V4 functional gate; not run during implementation.
    [Fact] public async Task V4_cockpit_uses_the_financial_source_eligible_attendance_and_audited_owner_target()
    {
        using var owner=await Staff();using var admin=await Staff(DemoSeed.AdminEmail);using var coach=await Mobile(AcademyRole.Coach);using var other=await Staff(DemoSeed.FutureOwnerEmail);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);
        var range=$"from={new DateOnly(today.Year,today.Month,1):yyyy-MM-dd}&to={today:yyyy-MM-dd}";
        var d=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-cockpit?"+range);
        var s=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/operating-statement?"+range);
        Assert.Equal(s.GetProperty("totalIncome").GetDecimal(),d.GetProperty("collections").GetProperty("net").GetDecimal());
        var seats=d.GetProperty("capacity");Assert.Equal(seats.GetProperty("items").EnumerateArray().Sum(g=>g.GetProperty("available").GetInt32()),seats.GetProperty("seats").GetInt32());
        Assert.All(seats.GetProperty("items").EnumerateArray(),g=>{Assert.True(g.GetProperty("available").GetInt32()>=0);if(g.GetProperty("baseMonthlyPrice").ValueKind==JsonValueKind.Null)Assert.Equal(JsonValueKind.Null,g.GetProperty("conditionalValue").ValueKind);});
        var attendance=d.GetProperty("attendance");if(attendance.GetProperty("recorded").GetInt32()==0)Assert.Equal(JsonValueKind.Null,attendance.GetProperty("percentage").ValueKind);
        foreach(var client in new[]{admin,coach})Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/reports/owner-cockpit?"+range)).StatusCode);
        var all=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-cockpit?allDates=true");Assert.False(all.GetProperty("collections").GetProperty("comparable").GetBoolean());Assert.Equal(JsonValueKind.Null,all.GetProperty("target").GetProperty("amount").ValueKind);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var active=await db.AcademyConfigurationVersions.AsNoTracking().SingleAsync(v=>v.AcademyId==DemoSeed.NogoomAcademyId&&v.State==ConfigurationState.Active);
        var settings=AcademyRuntimeSettings.Read(active.Payload);
        async Task Publish(AcademyRuntimeSettings body){var draft=await Send(owner,"/api/v1/manage/configuration/drafts",body);draft.EnsureSuccessStatusCode();var id=(await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();foreach(var target in new[]{"Validated","Staged","Active"}){var version=await db.AcademyConfigurationVersions.AsNoTracking().Where(v=>v.Id==id).Select(v=>v.Version).SingleAsync();(await Send(owner,$"/api/v1/manage/configuration/{id}/transition",new{target,version})).EnsureSuccessStatusCode();}}
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/configuration/drafts",settings with{MonthlyCollectionTarget=-1})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/configuration/drafts",settings with{MonthlyCollectionTarget=320000})).StatusCode);
        try{
            await Publish(settings with{MonthlyCollectionTarget=320000});
            var goal=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-cockpit?"+range);Assert.Equal(320000,goal.GetProperty("target").GetProperty("amount").GetDecimal());Assert.Equal(Math.Round(goal.GetProperty("collections").GetProperty("net").GetDecimal()*100/320000,1),goal.GetProperty("target").GetProperty("achievement").GetDecimal());
            var branch=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId).Select(b=>b.Id).FirstAsync();
            var scoped=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-cockpit?{range}&branchId={branch}");Assert.Equal(JsonValueKind.Null,scoped.GetProperty("target").GetProperty("amount").ValueKind);
            Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/reports/owner-cockpit?{range}&branchId={branch}")).StatusCode);
        }finally{await Publish(settings);}
    }
}
