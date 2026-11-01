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
    [Fact] public async Task Approved_home_target_scope_preferences_and_concurrent_revisions_are_isolated_and_audited()
    {
        using var owner=await Staff();using var admin=await Staff(DemoSeed.AdminEmail);using var other=await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy=DemoSeed.NogoomAcademyId;
        var original=AcademyRuntimeSettings.Read((await db.AcademyConfigurationVersions.AsNoTracking().SingleAsync(v=>v.AcademyId==academy&&v.State==ConfigurationState.Active)).Payload);
        async Task<int> Revision()=>await db.AcademyConfigurationVersions.AsNoTracking().Where(v=>v.AcademyId==academy&&v.State==ConfigurationState.Active).Select(v=>v.Revision).SingleAsync();
        async Task Restore(){var r=await Send(owner,"/api/v1/manage/configuration/drafts",original);r.EnsureSuccessStatusCode();var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();foreach(var target in new[]{"Validated","Staged","Active"}){var version=await db.AcademyConfigurationVersions.AsNoTracking().Where(v=>v.Id==id).Select(v=>v.Version).SingleAsync();(await Send(owner,$"/api/v1/manage/configuration/{id}/transition",new{target,version})).EnsureSuccessStatusCode();}}
        var branch=await db.Branches.Where(b=>b.AcademyId==academy&&b.IsActive).Select(b=>b.Id).FirstAsync();
        var collections=await db.Collections.CountAsync(c=>c.AcademyId==academy);
        try {
            var revision=await Revision();var body=new{amount=1000.25m,expectedRevision=revision,branchId=branch,from="2026-09-01",to="2026-09-30"};
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/configuration/collection-target",body,true)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(other,"/api/v1/manage/configuration/collection-target",body with{expectedRevision=await db.AcademyConfigurationVersions.AsNoTracking().Where(v=>v.AcademyId==DemoSeed.FutureAcademyId&&v.State==ConfigurationState.Active).Select(v=>v.Revision).SingleAsync()},true)).StatusCode);
            var saves=await Task.WhenAll(Send(owner,"/api/v1/manage/configuration/collection-target",body,true),Send(owner,"/api/v1/manage/configuration/collection-target",body,true));
            Assert.Single(saves,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(saves,r=>r.StatusCode==HttpStatusCode.Conflict);
            var scoped=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-cockpit?from=2026-09-01&to=2026-09-30&branchId={branch}");
            Assert.Equal(1000.25m,scoped.GetProperty("target").GetProperty("amount").GetDecimal());
            Assert.Equal(Math.Round(scoped.GetProperty("collections").GetProperty("net").GetDecimal()*100/1000.25m,1),scoped.GetProperty("target").GetProperty("achievement").GetDecimal());
            foreach(var query in new[]{"from=2026-09-01&to=2026-09-30",$"from=2026-09-02&to=2026-09-30&branchId={branch}","allDates=true"})
                Assert.Equal(JsonValueKind.Null,(await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-cockpit?"+query)).GetProperty("target").GetProperty("amount").ValueKind);
            revision=await Revision();
            var preferences=new{expectedRevision=revision,quickActionsAtTop=true,coachAssignmentTarget=3,highlightPortraits=new{coach="coach-02",member="guardian-02",player="child-02"}};
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/configuration/owner-home",preferences,true)).StatusCode);
            (await Send(owner,"/api/v1/manage/configuration/owner-home",preferences,true)).EnsureSuccessStatusCode();
            var active=AcademyRuntimeSettings.Read((await db.AcademyConfigurationVersions.AsNoTracking().SingleAsync(v=>v.AcademyId==academy&&v.State==ConfigurationState.Active)).Payload);
            Assert.True(active.OwnerHome!.QuickActionsAtTop);Assert.Equal(3,active.OwnerHome.CoachAssignmentTarget);Assert.Single(active.OwnerHome.Targets!);
            var home=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-cockpit?from=2026-09-01&to=2026-09-30&branchId={branch}");
            Assert.True(home.GetProperty("home").GetProperty("quickActionsAtTop").GetBoolean());
            Assert.Equal(3,home.GetProperty("home").GetProperty("coachAssignmentTarget").GetInt32());
            Assert.Equal("coach-02",home.GetProperty("home").GetProperty("highlightPortraits").GetProperty("coach").GetString());
            var otherHome=await other.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-cockpit?from=2026-09-01&to=2026-09-30");
            Assert.Equal(JsonValueKind.Null,otherHome.GetProperty("home").GetProperty("highlightPortraits").ValueKind);
            var beforeInvalid=await Revision();
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/configuration/owner-home",preferences with{expectedRevision=beforeInvalid,highlightPortraits=new{coach="https://example.test/a",member="guardian-02",player="child-02"}},true)).StatusCode);
            Assert.Equal(beforeInvalid,await Revision());
            var operations=(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-30&branchId={branch}")).GetProperty("operations");
            var expectedUnder=operations.GetProperty("coachPerformance").EnumerateArray().Where(c=>c.GetProperty("groupCount").GetInt32()<3).Select(c=>c.GetProperty("id").GetGuid()).Order().ToArray();
            var under=operations.GetProperty("underAssignedCoaches").EnumerateArray().ToArray();
            Assert.Equal(expectedUnder,under.Select(c=>c.GetProperty("id").GetGuid()).Order().ToArray());
            Assert.All(under,c=>{Assert.Equal(3,c.GetProperty("target").GetInt32());Assert.True(c.GetProperty("groupCount").GetInt32()<3);});
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/configuration/drafts",active with{OwnerHome=new(false,1)},false)).StatusCode);
            revision=await Revision();
            Assert.Equal(HttpStatusCode.NotFound,(await Send(owner,"/api/v1/manage/configuration/collection-target",body with{branchId=Guid.NewGuid(),expectedRevision=revision},true)).StatusCode);
            Assert.Equal(revision,await Revision());
            (await Send(owner,"/api/v1/manage/configuration/owner-home",preferences with{expectedRevision=revision},true)).EnsureSuccessStatusCode();
            Assert.Equal(revision,await Revision());
            Assert.Equal(collections,await db.Collections.CountAsync(c=>c.AcademyId==academy));
            Assert.True(await db.ConfigurationTransitions.AnyAsync(t=>t.AcademyId==academy&&t.To==ConfigurationState.Active));
        } finally {await Restore();}
    }
}
