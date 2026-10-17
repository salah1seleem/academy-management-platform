using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Owner_followup_concurrent_retries_are_single_and_live_training_entitlement_is_enforced()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await FollowUpGroup(db);
        var body=new OwnerFollowUpCreate(g.Id,"GroupCoachMissing",Guid.NewGuid());var results=await Task.WhenAll(Send(owner,"/api/v1/manage/owner-followups/",body),Send(owner,"/api/v1/manage/owner-followups/",body));
        Assert.Contains(results,r=>r.StatusCode==HttpStatusCode.Created);Assert.Contains(results,r=>r.StatusCode==HttpStatusCode.OK);var id=(await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(1,await db.OwnerFollowUps.CountAsync(f=>f.TrainingGroupId==g.Id));Assert.Equal(1,await db.OwnerFollowUpChanges.CountAsync(f=>f.FollowUpId==id));
        var license=await db.AcademyFeatureEntitlements.SingleAsync(l=>l.AcademyId==g.AcademyId&&l.Feature=="training");var enabled=license.Enabled;
        try{license.Enabled=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(owner,"/api/v1/manage/owner-followups/",body)).StatusCode);}
        finally{license.Enabled=enabled;await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);
    }
    private async Task<TrainingGroup> FollowUpGroup(FoundationDbContext db)
    {
        var source=await db.TrainingGroups.FirstAsync(g=>g.AcademyId==DemoSeed.NogoomAcademyId&&g.IsActive);
        var g=new TrainingGroup{Id=Guid.NewGuid(),AcademyId=source.AcademyId,ArabicName="مجموعة متابعة اصطناعية "+Guid.NewGuid().ToString("N")[..8],BranchId=source.BranchId,SportId=source.SportId,AgeCategoryId=source.AgeCategoryId,Capacity=20,CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow};db.TrainingGroups.Add(g);await db.SaveChangesAsync();return g;
    }
    [Fact] public async Task Owner_followup_lifecycle_retries_history_and_closure_never_change_source_or_finance()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await FollowUpGroup(db);
        var ownerId=await db.AcademyMemberships.Where(m=>m.AcademyId==g.AcademyId&&m.Role==AcademyRole.AcademyOwner&&m.IsActive).Select(m=>m.Id).FirstAsync();
        var collections=await db.Collections.CountAsync();var assignments=await db.StaffGroupAssignments.CountAsync();var payments=await db.PaymentRequests.CountAsync();
        var body=new OwnerFollowUpCreate(g.Id,"GroupLowOccupancy",Guid.NewGuid());var create=await Send(owner,"/api/v1/manage/owner-followups/",body);Assert.Equal(HttpStatusCode.Created,create.StatusCode);var id=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,(await Send(owner,"/api/v1/manage/owner-followups/",body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/owner-followups/",body with{Condition="GroupCoachMissing"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/owner-followups/",body with{RequestId=Guid.NewGuid()})).StatusCode);
        async Task<JsonElement> Read()=> (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");
        var initial=await Read();Assert.True(initial.GetProperty("conditionActive").GetBoolean());
        var assigned=new OwnerFollowUpWrite("Assigned",initial.GetProperty("version").GetUInt32(),Guid.NewGuid(),ownerId);
        Assert.Equal(HttpStatusCode.OK,(await Send(owner,$"/api/v1/manage/owner-followups/{id}",assigned)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Send(owner,$"/api/v1/manage/owner-followups/{id}",assigned)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,$"/api/v1/manage/owner-followups/{id}",assigned with{RequestId=Guid.NewGuid()})).StatusCode);
        foreach(var state in new[]{"InProgress","Snoozed","Closed","New","Rejected"})
        {
            var row=await Read();var update=new OwnerFollowUpWrite(state,row.GetProperty("version").GetUInt32(),Guid.NewGuid(),state=="New"?null:ownerId,DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3),"ملاحظة اختبار",state is "Closed" or "Rejected"?"أغلقت المتابعة البشرية مع بقاء حالة المجموعة":null);
            Assert.Equal(HttpStatusCode.OK,(await Send(owner,$"/api/v1/manage/owner-followups/{id}",update)).StatusCode);
        }
        var final=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}");Assert.Equal(7,final.GetProperty("history").GetArrayLength());Assert.True(final.GetProperty("item").GetProperty("conditionActive").GetBoolean());
        Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(payments,await db.PaymentRequests.CountAsync());Assert.Equal(assignments,await db.StaffGroupAssignments.CountAsync());Assert.True((await db.TrainingGroups.AsNoTracking().SingleAsync(x=>x.Id==g.Id)).IsActive);
        Assert.Equal(1,await db.OwnerFollowUps.CountAsync(f=>f.TrainingGroupId==g.Id));
        g.Capacity=null;await db.SaveChangesAsync();Assert.Equal(JsonValueKind.Null,(await Read()).GetProperty("conditionActive").ValueKind);g.Capacity=20;await db.SaveChangesAsync();
        var history=await db.OwnerFollowUpChanges.FirstAsync(c=>c.FollowUpId==id);history.AfterJson="{}";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OwnerFollowUpChanges\" SET \"AfterJson\"='{{}}'::jsonb WHERE \"FollowUpId\"={id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OwnerFollowUps\" SET \"Condition\"='GroupCoachMissing' WHERE \"Id\"={id}"));
    }
    [Fact] public async Task Owner_followups_validate_owner_scope_dates_reasons_and_isolate_academy_and_roles()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);using var admin=await Staff(DemoSeed.AdminEmail);using var coach=await Mobile(AcademyRole.Coach);using var guardian=await Mobile(AcademyRole.Guardian);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await FollowUpGroup(db);var body=new OwnerFollowUpCreate(g.Id,"GroupCoachMissing",Guid.NewGuid());
        foreach(var c in new[]{admin,coach,guardian}){Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/manage/owner-followups/options")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(c,"/api/v1/manage/owner-followups/",body)).StatusCode);}
        Assert.Equal(HttpStatusCode.NotFound,(await Send(other,"/api/v1/manage/owner-followups/",body)).StatusCode);
        var badOwner=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.FutureAcademyId&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/owner-followups/",body with{AssignedToMembershipId=badOwner})).StatusCode);
        var r=await Send(owner,"/api/v1/manage/owner-followups/",body);r.EnsureSuccessStatusCode();var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var row=(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");var version=row.GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);
        foreach(var s in new[]{"Assigned","InProgress","Closed","Rejected","Snoozed","not-a-state"})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,$"/api/v1/manage/owner-followups/{id}",new OwnerFollowUpWrite(s,version,Guid.NewGuid()))).StatusCode);
        var list=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups?allDates=true");Assert.Contains(list.GetProperty("items").EnumerateArray(),f=>f.GetProperty("id").GetGuid()==id);Assert.Equal(JsonValueKind.Null,list.GetProperty("from").ValueKind);
        var blank=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups?from=2000-01-01&to=2000-01-02");Assert.Empty(blank.GetProperty("items").EnumerateArray());
        var defaults=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups");Assert.EndsWith("-01",defaults.GetProperty("from").GetString());
        g.Capacity=null;await db.SaveChangesAsync();Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options")).GetProperty("candidates").EnumerateArray(),c=>c.GetProperty("source").GetProperty("groupId").GetGuid()==g.Id&&c.GetProperty("condition").GetString()=="GroupLowOccupancy");
        g.IsActive=false;await db.SaveChangesAsync();Assert.False((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item").GetProperty("conditionActive").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/owner-followups/",body with{RequestId=Guid.NewGuid(),Condition="GroupLowOccupancy"})).StatusCode);
    }
}
