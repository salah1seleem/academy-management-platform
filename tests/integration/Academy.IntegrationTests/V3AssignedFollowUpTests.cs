using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private async Task DelegateFixture(Func<HttpClient,HttpClient,FoundationDbContext,TrainingGroup,Guid,Task> action)
    {
        using var owner = await Staff(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await FollowUpGroup(db);
        var m = await db.AcademyMemberships.SingleAsync(m => m.AcademyId == group.AcademyId && m.User.Email == DemoSeed.AdminEmail); var before = (m.AllBranches,m.AllSports,m.UseActionGrants);
        var grant = await db.StaffActionGrants.SingleOrDefaultAsync(g => g.MembershipId == m.Id && g.Module == "followups"); var old = grant == null ? null : JsonSerializer.Serialize(grant); var added = grant == null;
        grant ??= new() { Id = Guid.NewGuid(), AcademyId = m.AcademyId, MembershipId = m.Id, Module = "followups" };
        var branches = await db.MembershipBranchAccesses.Where(b => b.MembershipId == m.Id).ToListAsync(); var sports = await db.MembershipSportAccesses.Where(b => b.MembershipId == m.Id).ToListAsync(); var oldBranches = branches.ToDictionary(x=>x.Id,x=>x.IsActive); var oldSports = sports.ToDictionary(x=>x.Id,x=>x.IsActive);
        var branch = branches.FirstOrDefault(b=>b.BranchId==group.BranchId); var sport = sports.FirstOrDefault(s=>s.SportId==group.SportId);
        if (branch == null) { branch = new() { Id=Guid.NewGuid(), AcademyId=m.AcademyId,MembershipId=m.Id,BranchId=group.BranchId }; db.MembershipBranchAccesses.Add(branch); }
        if (sport == null) { sport = new() { Id=Guid.NewGuid(),AcademyId=m.AcademyId,MembershipId=m.Id,SportId=group.SportId }; db.MembershipSportAccesses.Add(sport); }
        try {
            m.AllBranches=false;m.AllSports=false;m.UseActionGrants=true;
            foreach(var b in branches)b.IsActive=b.BranchId==group.BranchId; foreach(var s in sports)s.IsActive=s.SportId==group.SportId; branch.IsActive=true;sport.IsActive=true;
            grant.Read=true;grant.Edit=true;grant.Add=false;grant.Delete=false;grant.IsActive=true;if(added)db.StaffActionGrants.Add(grant);await db.SaveChangesAsync();
            using var admin=await Staff(DemoSeed.AdminEmail); await action(owner,admin,db,group,m.Id);
        } finally {
            m.AllBranches=before.AllBranches;m.AllSports=before.AllSports;m.UseActionGrants=before.UseActionGrants;
            foreach(var b in branches)b.IsActive=oldBranches[b.Id];foreach(var s in sports)s.IsActive=oldSports[s.Id];if(!oldBranches.ContainsKey(branch.Id))db.MembershipBranchAccesses.Remove(branch);if(!oldSports.ContainsKey(sport.Id))db.MembershipSportAccesses.Remove(sport);
            if(added)db.StaffActionGrants.Remove(grant);else db.Entry(grant).CurrentValues.SetValues(JsonSerializer.Deserialize<StaffActionGrant>(old!)!);await db.SaveChangesAsync();
        }
    }
    private static async Task<Guid> AssignFollowUp(HttpClient owner, TrainingGroup group, Guid member)
    { var r=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(group.Id,"GroupCoachMissing",Guid.NewGuid(),member));r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }
    [Fact] public async Task Assigned_followup_own_inbox_transition_replay_and_manual_closure_leave_sources_and_money_unchanged() => await DelegateFixture(async(owner,admin,db,group,member)=>{
        var id=await AssignFollowUp(owner,group,member);var otherGroup=await FollowUpGroup(db);var unassigned=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(otherGroup.Id,"GroupCoachMissing",Guid.NewGuid()));unassigned.EnsureSuccessStatusCode();var hidden=(await unassigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var report=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");var counts=new[]{await db.StaffGroupAssignments.CountAsync(),await db.PlayerAttendances.CountAsync(),await db.Collections.CountAsync()};
        var list=await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-followups?allDates=true");Assert.Contains(list.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id);Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==hidden);Assert.False(list.TryGetProperty("owners",out _));
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/my-followups/{hidden}?academyId={group.AcademyId}")).StatusCode);
        async Task<JsonElement> Read()=>await admin.GetFromJsonAsync<JsonElement>($"/api/v1/my-followups/{id}");
        var d=await Read();Assert.True(d.GetProperty("canEdit").GetBoolean());var body=new AssignedFollowUpWrite("InProgress",d.GetProperty("item").GetProperty("version").GetUInt32(),Guid.NewGuid(),Note:"تمت مراجعة جدول المجموعة — اختبار اصطناعي");
        Assert.Equal(HttpStatusCode.OK,(await Send(admin,$"/api/v1/my-followups/{id}",body)).StatusCode);Assert.True((await (await Send(admin,$"/api/v1/my-followups/{id}",body)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        var forged=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OwnerFollowUpChanges" ("Id","AcademyId","ActorMembershipId","FollowUpId","RequestId","RequestHash","BeforeJson","AfterJson","IsActive","CreatedAtUtc","UpdatedAtUtc")
            VALUES ({Guid.NewGuid()},{group.AcademyId},{member},{id},{Guid.NewGuid()},{new string('a',64)},{"{}"}::jsonb,{"{}"}::jsonb,true,now(),now())
            """));Assert.Equal("23514",forged.SqlState);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,$"/api/v1/my-followups/{id}",body with{RequestId=Guid.NewGuid()})).StatusCode);
        d=await Read();var v=d.GetProperty("item").GetProperty("version").GetUInt32();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(admin,$"/api/v1/my-followups/{id}",new AssignedFollowUpWrite("Closed",v,Guid.NewGuid()))).StatusCode);
        var close=new AssignedFollowUpWrite("Closed",v,Guid.NewGuid(),ResolutionReason:"أغلقت المتابعة البشرية فقط، إسناد المدرب ما زال يحتاج إجراء مستقلًا");(await Send(admin,$"/api/v1/my-followups/{id}",close)).EnsureSuccessStatusCode();(await Send(admin,$"/api/v1/my-followups/{id}",close)).EnsureSuccessStatusCode();
        d=await Read();Assert.False(d.GetProperty("canEdit").GetBoolean());Assert.True(d.GetProperty("item").GetProperty("conditionActive").GetBoolean());Assert.Equal(3,d.GetProperty("history").GetArrayLength());
        Assert.Equal(member,(await db.OwnerFollowUps.AsNoTracking().SingleAsync(f=>f.Id==id)).AssignedToMembershipId);Assert.Equal(counts,new[]{await db.StaffGroupAssignments.CountAsync(),await db.PlayerAttendances.CountAsync(),await db.Collections.CountAsync()});Assert.Equal(report,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    });
    [Fact] public async Task Assigned_followup_revokes_read_edit_branch_sport_and_reassignment_before_replay() => await DelegateFixture(async(owner,admin,db,group,member)=>{
        var id=await AssignFollowUp(owner,group,member);var row=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/my-followups/{id}");var body=new AssignedFollowUpWrite("InProgress",row.GetProperty("item").GetProperty("version").GetUInt32(),Guid.NewGuid());(await Send(admin,$"/api/v1/my-followups/{id}",body)).EnsureSuccessStatusCode();
        var grant=await db.StaffActionGrants.SingleAsync(g=>g.MembershipId==member&&g.Module=="followups");grant.Edit=false;await db.SaveChangesAsync();Assert.False((await admin.GetFromJsonAsync<JsonElement>($"/api/v1/my-followups/{id}")).GetProperty("canEdit").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"/api/v1/my-followups/{id}",body)).StatusCode);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("owners").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==member);
        grant.Read=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/my-followups")).StatusCode);grant.Read=true;grant.Edit=true;await db.SaveChangesAsync();
        var branch=await db.MembershipBranchAccesses.SingleAsync(b=>b.MembershipId==member&&b.BranchId==group.BranchId);branch.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/my-followups/{id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await Send(admin,$"/api/v1/my-followups/{id}",body)).StatusCode);branch.IsActive=true;await db.SaveChangesAsync();
        var sport=await db.MembershipSportAccesses.SingleAsync(b=>b.MembershipId==member&&b.SportId==group.SportId);sport.IsActive=false;await db.SaveChangesAsync();Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-followups?allDates=true")).GetProperty("items").EnumerateArray());sport.IsActive=true;await db.SaveChangesAsync();
        var detail=(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");(await Send(owner,$"/api/v1/manage/owner-followups/{id}",new OwnerFollowUpWrite("New",detail.GetProperty("version").GetUInt32(),Guid.NewGuid()))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await Send(admin,$"/api/v1/my-followups/{id}",body)).StatusCode);
    });
    [Fact] public async Task Assigned_followup_denies_other_roles_other_tenants_owner_endpoints_and_unlicensed_training() => await DelegateFixture(async(owner,admin,db,group,member)=>{
        var id=await AssignFollowUp(owner,group,member);using var other=await Staff(DemoSeed.FutureOwnerEmail);using var coach=await Mobile(AcademyRole.Coach);using var guardian=await Mobile(AcademyRole.Guardian);
        foreach(var c in new[]{owner,other,coach,guardian})Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync($"/api/v1/my-followups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(group.Id,"GroupLowOccupancy",Guid.NewGuid(),member))).StatusCode);
        var license=await db.AcademyFeatureEntitlements.SingleAsync(l=>l.AcademyId==group.AcademyId&&l.Feature=="training");try{license.Enabled=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/my-followups")).StatusCode);}finally{license.Enabled=true;await db.SaveChangesAsync();}
        Assert.Contains(AcademyPermissions.FollowUpRead,(await admin.GetFromJsonAsync<string[]>("/api/v1/my-permissions"))!);Assert.DoesNotContain(AcademyPermissions.OwnerDashboardRead,(await admin.GetFromJsonAsync<string[]>("/api/v1/my-permissions"))!);
        foreach(var operation in new[]{"Add","Delete"}) {
            var invalid=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="followups",m=="followups"&&operation=="Add",m=="followups",m=="followups"&&operation=="Delete")).ToArray();
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,$"/api/v1/manage/staff/{member}/access",new StaffAccessWrite(false,[group.BranchId],invalid,AllSports:false,Sports:[group.SportId]),true)).StatusCode);
        }
    });
    [Fact] public async Task Assigned_followup_second_academy_authorized_delegate_cannot_read_or_update_first_academy() => await DelegateFixture(async(owner,admin,db,group,member)=>{
        var id=await AssignFollowUp(owner,group,member);using var otherOwner=await Staff(DemoSeed.FutureOwnerEmail);var email="followup-other-"+Guid.NewGuid().ToString("N")+"@example.test";
        var r=await Send(otherOwner,"/api/v1/manage/staff/",new StaffWrite("مسؤول أكاديمية ثانية — اختبار اصطناعي",email,"010999"+Random.Shared.Next(10000,99999),"Demo-Only-123!",AcademyRole.AcademyAdmin,"متابعة تشغيل"));r.EnsureSuccessStatusCode();var otherMember=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="followups",false,m=="followups",false)).ToArray();(await Send(otherOwner,$"/api/v1/manage/staff/{otherMember}/access",new StaffAccessWrite(true,[],actions,AllSports:true,Sports:[]),true)).EnsureSuccessStatusCode();
        using var second=await Staff(email);(await second.GetAsync("/api/v1/my-followups?allDates=true")).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/v1/my-followups/{id}?academyId={group.AcademyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Send(second,$"/api/v1/my-followups/{id}",new AssignedFollowUpWrite("InProgress",1,Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(group.Id,"GroupLowOccupancy",Guid.NewGuid(),otherMember))).StatusCode);
    });
}
