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
    [Fact] public async Task V4_related_record_followups_reuse_history_idempotency_and_scopes_without_a_fake_group()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);using var coach=await Mobile(AcademyRole.Coach);
        var name="منسق متابعة اصطناعي "+Guid.NewGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.IsActive).Select(b=>b.Id).FirstAsync();var ownerId=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.Role==AcademyRole.AcademyOwner&&m.IsActive).Select(m=>m.Id).FirstAsync();
        var creation=await Send(owner,"/api/v1/manage/employees/",new{name,phone="010999"+Random.Shared.Next(10000,99999),jobTitle="تنسيق تجريبي",branchId=branch,monthlySalary=4000m,contractStart="2026-11-01",contractEnd=(string?)null,membershipId=(Guid?)null,photoAssetId=(Guid?)null,isActive=true});creation.EnsureSuccessStatusCode();var employee=(await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var options=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/records?kind=Employee&search="+Uri.EscapeDataString(name));Assert.Single(options.GetProperty("items").EnumerateArray());
        var body=new{kind="Employee",sourceId=employee,branchId=(Guid?)branch,sportId=(Guid?)null,requestId=Guid.NewGuid(),assignedToMembershipId=(Guid?)ownerId,priority=2,note="مراجعة مستندات العقد دون صرف أو خصم تلقائي"};
        var response=await Send(owner,"/api/v1/manage/owner-followups/records",body);Assert.Equal(HttpStatusCode.Created,response.StatusCode);var id=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Send(owner,"/api/v1/manage/owner-followups/records",body)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/owner-followups/records",new{kind="Employee",sourceId=employee,branchId=branch,requestId=Guid.NewGuid()})).StatusCode);
        var details=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}");var row=details.GetProperty("item");Assert.Equal(JsonValueKind.Null,row.GetProperty("trainingGroupId").ValueKind);Assert.Equal("Employee",row.GetProperty("sourceKind").GetString());Assert.Equal(name,row.GetProperty("source").GetProperty("name").GetString());Assert.Equal(2,row.GetProperty("priority").GetInt32());Assert.False(row.GetProperty("source").TryGetProperty("enrollments",out _));Assert.Single(details.GetProperty("history").EnumerateArray());
        var list=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups?allDates=true&priority=2&branchId={branch}&search={Uri.EscapeDataString(name)}");Assert.Equal(1,list.GetProperty("count").GetInt32());
        foreach(var client in new[]{other,coach})Assert.True((await client.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.NotFound,(await Send(other,"/api/v1/manage/owner-followups/records",body)).StatusCode);
        (await Send(owner,$"/api/v1/manage/owner-followups/{id}",new{state="Closed",version=row.GetProperty("version").GetUInt32(),requestId=Guid.NewGuid(),assignedToMembershipId=ownerId,resolutionReason="استكملت مراجعة اصطناعية"})).EnsureSuccessStatusCode();
        var history=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}");Assert.Equal(2,history.GetProperty("history").GetArrayLength());Assert.Equal("Closed",history.GetProperty("item").GetProperty("state").GetString());
    }
}
