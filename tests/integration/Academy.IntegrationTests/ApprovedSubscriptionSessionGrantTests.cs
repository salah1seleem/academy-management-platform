using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Api.Slice8;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionAdjustmentIntegrationTests
{
    private async Task<SubscriptionAdjustmentResult> GrantSessions(Scenario s, int count = 4, DateOnly? end = null, uint? version = null, string? key = null)
    {
        await using var scope = Scope(); var db = Db(scope); var academy = await Academy(s);
        var member = await db.AcademyMemberships.SingleAsync(m => m.AcademyId == academy && m.Role == AcademyRole.AcademyOwner && m.User.Email == (academy == DemoSeed.NogoomAcademyId ? DemoSeed.OwnerEmail : DemoSeed.FutureOwnerEmail));
        return await scope.ServiceProvider.GetRequiredService<SubscriptionAdjustmentService>().GrantSessionsAsync(academy, member.UserId, member.Id, s.PeriodId, count,
            end ?? s.EndDate.AddDays(7), "إضافة حصص اصطناعية", version ?? (await Period(s)).Version, key ?? Guid.NewGuid().ToString());
    }

    [Fact] public async Task Approved_session_grant_updates_balance_and_whole_end_with_original_finance_and_audit_preserved()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined, linkGuardian: true); using var owner = await Staff(DemoSeed.OwnerEmail);
        var money = await CollectionSnapshot(s); var refund = await owner.GetStringAsync($"/api/v1/people/players/{s.PlayerId}/refunds");
        var result = await GrantSessions(s); var period = await Period(s);
        Assert.Equal(14, period.RemainingSessions); Assert.Equal(10, period.InitialSessions); Assert.Equal(s.EndDate.AddDays(7), period.EndDate);
        Assert.Equal(321m, period.PriceSnapshot); Assert.Equal(s.CollectionId, period.CollectionId); Assert.Equal(money, await CollectionSnapshot(s));
        Assert.Equal(refund, await owner.GetStringAsync($"/api/v1/people/players/{s.PlayerId}/refunds"));
        await using var scope = Scope(); var db = Db(scope);
        var audit = await db.SubscriptionAdjustments.AsNoTracking().SingleAsync(a => a.Id == result.AdjustmentId);
        Assert.Equal(4, audit.SessionsDelta); Assert.Equal(10, audit.OldRemainingSessions); Assert.Equal(14, audit.NewRemainingSessions);
        Assert.Equal(s.EndDate, audit.OldEndDate); Assert.Equal(period.EndDate, audit.NewEndDate); Assert.Null(audit.DaysDelta);
        Assert.Equal(await OwnerId(DemoSeed.NogoomAcademyId), audit.PerformedByUserId); Assert.NotEqual(default, audit.PerformedAtUtc);
        Assert.Equal(10, await db.SubscriptionPlans.Where(p => p.Id == s.PlanId).Select(p => p.SessionCount).SingleAsync());
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{s.PeriodId}");
        Assert.True(detail.GetProperty("canGrantSessions").GetBoolean()); Assert.Equal(4, detail.GetProperty("adjustments")[0].GetProperty("sessionsDelta").GetInt32());
        using var guardian = await Guardian();
        var family = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/subscriptions/periods/{s.PeriodId}/adjustments");
        Assert.Equal(14, family.GetProperty("remainingSessions").GetInt32());
        Assert.Equal(4, family.GetProperty("adjustments")[0].GetProperty("sessionsDelta").GetInt32());
    }

    [Fact] public async Task Approved_session_grant_parallel_replay_is_once_and_changed_intent_or_period_is_rejected()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined, linkGuardian: true); var key = Guid.NewGuid().ToString();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => GrantSessions(s, version:s.Version, key:key)));
        Assert.Single(outcomes.Select(r => r.AdjustmentId).Distinct()); Assert.Single(outcomes, r => !r.Replay);
        Assert.Equal(14, (await Period(s)).RemainingSessions); Assert.Equal(1, await AdjustmentCount(s, SubscriptionAdjustmentType.SessionsAdded));
        await Assert.ThrowsAsync<AdjustmentConflictException>(() => GrantSessions(s, count:5, key:key));
        await Assert.ThrowsAsync<AdjustmentConflictException>(() => GrantSessions(s, end:s.EndDate.AddDays(8), key:key));
        var other = await NewScenario(SubscriptionPlanType.Combined);
        await Assert.ThrowsAsync<AdjustmentConflictException>(() => GrantSessions(other, key:key));
        await using var scope = Scope(); var db = Db(scope);
        var notices = await db.AcademyNotices.Where(n => n.EventKey == "SessionsAdded" && n.TargetId == s.PeriodId).ToArrayAsync();
        Assert.Single(notices); Assert.Contains("4", notices[0].Message); Assert.Contains(s.EndDate.AddDays(7).ToString("yyyy-MM-dd"), notices[0].Message); Assert.DoesNotContain("{", notices[0].Message);
        Assert.Equal("SubscriptionPeriod", notices[0].TargetKind);
    }

    [Fact] public async Task Approved_session_grant_requires_explicit_admin_leaf_and_denies_other_roles_or_academies()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined, linkGuardian:true);
        object Body() => new { sessions=2, endDate=s.EndDate.AddDays(7), reason="إضافة مخولة اصطناعية", expectedVersion=(uint)s.Version };
        var path=$"/api/v1/subscriptions/periods/{s.PeriodId}/sessions";
        using var coach=await Staff(DemoSeed.CoachEmail); using var guardian=await Guardian(); using var foreign=await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(coach,path,Body(),Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(guardian,path,Body(),Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(foreign,path,Body(),Guid.NewGuid().ToString())).StatusCode);
        await using var scope=Scope(); var db=Db(scope);
        var admin=await db.AcademyMemberships.SingleAsync(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.User.Email==DemoSeed.AdminEmail);
        var prior=await db.StaffGrants.SingleOrDefaultAsync(g=>g.MembershipId==admin.Id&&g.Permission=="workflow.subscriptions.sessions");
        var oldAllowed=prior?.Allowed; var grant=prior??new StaffGrant{Id=Guid.NewGuid(),AcademyId=admin.AcademyId,MembershipId=admin.Id,Permission="workflow.subscriptions.sessions",Allowed=false};
        if(prior==null)db.StaffGrants.Add(grant);
        var useActions = admin.UseActionGrants;
        var priorAction = await db.StaffActionGrants.SingleOrDefaultAsync(a => a.MembershipId == admin.Id && a.Module == "subscriptions");
        var action = priorAction ?? new StaffActionGrant { Id = Guid.NewGuid(), AcademyId = admin.AcademyId, MembershipId = admin.Id, Module = "subscriptions" };
        var previousAction = (action.Read, action.Add, action.Edit, action.Delete, action.IsActive);
        if (priorAction is null) db.StaffActionGrants.Add(action);
        admin.UseActionGrants = true;
        action.Read = action.Edit = action.IsActive = true;
        action.Add = action.Delete = false; // The grant edits an existing period; Add must not be required.
        grant.Allowed=false;await db.SaveChangesAsync();
        try {
            using(var denied=await Staff(DemoSeed.AdminEmail)) Assert.Equal(HttpStatusCode.Forbidden,(await Post(denied,path,Body(),Guid.NewGuid().ToString())).StatusCode);
            grant.Allowed=true;await db.SaveChangesAsync();
            using(var allowed=await Staff(DemoSeed.AdminEmail)) (await Post(allowed,path,Body(),Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
            Assert.Equal(12,(await Period(s)).RemainingSessions);
            grant.Allowed=false;await db.SaveChangesAsync();
            using(var revoked=await Staff(DemoSeed.AdminEmail)) Assert.Equal(HttpStatusCode.Forbidden,(await Post(revoked,path,new{sessions=2,endDate=s.EndDate.AddDays(8),reason="طلب بعد المنع",expectedVersion=(await Period(s)).Version},Guid.NewGuid().ToString())).StatusCode);
            Assert.Equal(12,(await Period(s)).RemainingSessions);
        } finally {
            if(prior==null)db.StaffGrants.Remove(grant);else grant.Allowed=oldAllowed!.Value;
            admin.UseActionGrants = useActions;
            if (priorAction is null) db.StaffActionGrants.Remove(action);
            else (action.Read, action.Add, action.Edit, action.Delete, action.IsActive) = previousAction;
            await db.SaveChangesAsync();
        }
    }

    [Fact] public async Task Approved_session_grant_validates_count_date_reason_stale_version_and_overlap_without_mutation()
    {
        var s=await NewScenario(SubscriptionPlanType.Combined);using var owner=await Staff(DemoSeed.OwnerEmail);var path=$"/api/v1/subscriptions/periods/{s.PeriodId}/sessions";
        async Task<HttpStatusCode> Send(int count,DateOnly end,string reason="سبب اختبار",uint? version=null)=>(await Post(owner,path,new{sessions=count,endDate=end,reason,expectedVersion=version??s.Version},Guid.NewGuid().ToString())).StatusCode;
        foreach(var count in new[]{0,-1,10001})Assert.Equal(HttpStatusCode.UnprocessableEntity,await Send(count,s.EndDate.AddDays(7)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,await Send(2,s.EndDate.AddDays(7)," "));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,await Send(2,Today.AddDays(-1)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,await Send(2,s.EndDate.AddDays(-1)));
        Assert.Equal(HttpStatusCode.Conflict,await Send(2,s.EndDate.AddDays(7),version:s.Version+1));
        await AddPaidPeriod(s,s.EndDate.AddDays(1),s.EndDate.AddDays(30),SubscriptionPeriodStatus.Scheduled);
        Assert.Equal(HttpStatusCode.Conflict,await Send(2,s.EndDate.AddDays(7)));
        Assert.Equal(10,(await Period(s)).RemainingSessions);Assert.Equal(s.EndDate,(await Period(s)).EndDate);Assert.Equal(0,await AdjustmentCount(s,SubscriptionAdjustmentType.SessionsAdded));
    }

    [Fact] public async Task Approved_session_grant_preserves_existing_overlap_and_rejects_only_new_extension_overlap()
    {
        var existing = await NewScenario(SubscriptionPlanType.Combined);
        await AddPaidPeriod(existing, Today.AddDays(-2), existing.EndDate.AddDays(-1), SubscriptionPeriodStatus.Active);
        await GrantSessions(existing);
        Assert.Equal(14, (await Period(existing)).RemainingSessions);
        Assert.Equal(existing.EndDate.AddDays(7), (await Period(existing)).EndDate);

        var unchangedEnd = await NewScenario(SubscriptionPlanType.Combined);
        await AddPaidPeriod(unchangedEnd, unchangedEnd.EndDate.AddDays(1), unchangedEnd.EndDate.AddDays(30), SubscriptionPeriodStatus.Scheduled);
        await GrantSessions(unchangedEnd, end: unchangedEnd.EndDate);
        Assert.Equal(14, (await Period(unchangedEnd)).RemainingSessions);
        await Assert.ThrowsAsync<AdjustmentConflictException>(() => GrantSessions(unchangedEnd));
    }

    [Fact] public async Task Approved_session_grant_handles_session_only_expiry_and_frozen_state_without_converting_duration_or_cancelled_periods()
    {
        var expired=await NewScenario(SubscriptionPlanType.Sessions,SubscriptionPeriodStatus.Expired);
        await using(var scope=Scope()){var p=await Db(scope).SubscriptionPeriods.SingleAsync(p=>p.Id==expired.PeriodId);p.RemainingSessions=0;await Db(scope).SaveChangesAsync();}
        await GrantSessions(expired,2,Today.AddDays(14));var live=await Period(expired);
        Assert.Equal(2,live.RemainingSessions);Assert.Equal(Today.AddDays(14),live.EndDate);Assert.Equal(SubscriptionPeriodStatus.Active,live.Status);Assert.Equal(10,live.InitialSessions);
        var frozen=await NewScenario(SubscriptionPlanType.Combined);await Freeze(frozen);await GrantSessions(frozen);
        Assert.Equal(SubscriptionPeriodStatus.Frozen,(await Period(frozen)).Status);Assert.Equal(Today.AddDays(-1),(await Period(frozen)).FrozenFromDate);
        var duration=await NewScenario();await Assert.ThrowsAsync<AdjustmentValidationException>(()=>GrantSessions(duration));
        var cancelled=await NewScenario(SubscriptionPlanType.Combined,SubscriptionPeriodStatus.Cancelled);await Assert.ThrowsAsync<AdjustmentConflictException>(()=>GrantSessions(cancelled));
    }

    [Fact] public async Task Approved_session_grant_attendance_consumes_and_restores_above_original_quota_once()
    {
        var s=await NewScenario(SubscriptionPlanType.Combined);await GrantSessions(s);var attendance=await Attend(s,Today);
        Assert.True(attendance.SessionConsumed);Assert.Equal(13,attendance.RemainingSessions);
        await using var scope=Scope();var db=Db(scope);var row=await db.PlayerAttendances.AsNoTracking().SingleAsync(a=>a.SportEnrollmentId==s.EnrollmentId);
        var service=scope.ServiceProvider.GetRequiredService<AttendanceService>();
        var result=(await service.SavePlayersAsync(DemoSeed.NogoomAcademyId,row.TrainingSessionId,await OwnerId(DemoSeed.NogoomAcademyId),[new(s.EnrollmentId,AttendanceStatus.Excused,row.Version)])).Single();
        Assert.Equal(14,result.RemainingSessions);Assert.False(result.SessionConsumed);Assert.Equal(14,(await Period(s)).RemainingSessions);
        await service.SavePlayersAsync(DemoSeed.NogoomAcademyId,row.TrainingSessionId,await OwnerId(DemoSeed.NogoomAcademyId),[new(s.EnrollmentId,AttendanceStatus.Excused)]);
        Assert.Equal(14,(await Period(s)).RemainingSessions);Assert.Equal(2,await MovementCount(s));
        var expired=await Attend(s,s.EndDate.AddDays(8));Assert.False(expired.SessionConsumed);Assert.Equal(14,(await Period(s)).RemainingSessions);
    }

    [Fact] public async Task Approved_session_grant_pending_renewal_activates_after_adjusted_whole_end()
    {
        var s=await NewScenario(SubscriptionPlanType.Combined,linkGuardian:true);var payment=await Renew(s);
        await GrantSessions(s);await Simulate(payment);
        await using var scope=Scope();var db=Db(scope);
        var renewal=await db.SubscriptionPeriods.SingleAsync(p=>p.Collection.PaymentRequestId==payment);
        Assert.Equal(s.EndDate.AddDays(8),renewal.StartDate);Assert.Equal(s.EnrollmentId,renewal.SportEnrollmentId);
        Assert.Equal(2,await db.SubscriptionPeriods.CountAsync(p=>p.SportEnrollmentId==s.EnrollmentId));
        Assert.Equal(14,(await Period(s)).RemainingSessions);
    }

    [Fact] public async Task Approved_session_grant_audit_cannot_be_overwritten_by_application_save()
    {
        var s=await NewScenario(SubscriptionPlanType.Combined);var result=await GrantSessions(s);
        await using var scope=Scope();var db=Db(scope);var audit=await db.SubscriptionAdjustments.SingleAsync(a=>a.Id==result.AdjustmentId);
        audit.SessionsDelta=1;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        await using var readScope=Scope();Assert.Equal(4,await Db(readScope).SubscriptionAdjustments.Where(a=>a.Id==result.AdjustmentId).Select(a=>a.SessionsDelta).SingleAsync());
    }

    [Fact] public async Task Scoped_imported_period_adjustment_keeps_hidden_owner_import_source_without_widening_access()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined);
        var jobId = Guid.NewGuid();
        try
        {
            Guid adminUser, adminMember, branch;
            await using (var setup = Scope())
            {
                var db = Db(setup);
                var admin = await db.AcademyMemberships.SingleAsync(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.User.Email == DemoSeed.AdminEmail);
                var owner = await db.AcademyMemberships.SingleAsync(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.AcademyOwner && m.User.Email == DemoSeed.OwnerEmail);
                adminUser = admin.UserId; adminMember = admin.Id;
                branch = await db.SportEnrollments.Where(e => e.Id == s.EnrollmentId).Select(e => e.BranchId).SingleAsync();
                var now = DateTimeOffset.UtcNow;
                db.AcademyImportJobs.Add(new AcademyImportJob { Id = jobId, AcademyId = owner.AcademyId, ActorUserId = owner.UserId, MembershipId = owner.Id,
                    SourceSystem = "scoped-import-fixture", FileHash = new string('a', 64), ReferenceStamp = new string('b', 64), IdempotencyKey = jobId.ToString(),
                    ApprovedBranches = JsonSerializer.Serialize(new[] { branch }), GuardianResolutions = "[]", CutoffDate = Today, State = ImportJobState.Completed,
                    RowCount = 1, PublishedRows = 1, GuardianLinksApproved = true, OpeningBalancesApproved = true, CompletedAtUtc = now, ExpiresAtUtc = now.AddHours(1), CreatedAtUtc = now, UpdatedAtUtc = now });
                var period = await db.SubscriptionPeriods.SingleAsync(p => p.Id == s.PeriodId);
                period.Origin = SubscriptionOrigin.ImportedHistorical; period.ImportJobId = jobId; period.CollectionId = null;
                await db.SaveChangesAsync();
            }
            var finance = await CollectionSnapshot(s);
            await using var scoped = Scope(); var scopedDb = Db(scoped);
            scopedDb.BindRequestScope(DemoSeed.NogoomAcademyId, adminUser, adminMember, [branch], module: "subscriptions", operation: "Edit");
            Assert.False(await scopedDb.AcademyImportJobs.AnyAsync(j => j.Id == jobId));
            var before = await scopedDb.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.Id == s.PeriodId);
            await scoped.ServiceProvider.GetRequiredService<SubscriptionAdjustmentService>().AdjustDaysAsync(DemoSeed.NogoomAcademyId, adminUser, s.PeriodId, 1, true, "تعديل فترة مستوردة اصطناعي", before.Version, Guid.NewGuid().ToString());
            var saved = await scopedDb.SubscriptionPeriods.SingleAsync(p => p.Id == s.PeriodId);
            Assert.Equal(s.EndDate.AddDays(1), saved.EndDate); Assert.Equal(jobId, saved.ImportJobId); Assert.Null(saved.CollectionId);
            Assert.False(await scopedDb.AcademyImportJobs.AnyAsync(j => j.Id == jobId));
            Assert.Equal(finance, await CollectionSnapshot(s));
            saved.ImportJobId = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => scopedDb.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup = Scope(); var db = Db(cleanup);
            await db.SubscriptionAdjustments.Where(a => a.SubscriptionPeriodId == s.PeriodId).ExecuteDeleteAsync();
            await db.SubscriptionPeriods.Where(p => p.Id == s.PeriodId).ExecuteDeleteAsync();
            await db.AcademyImportJobs.Where(j => j.Id == jobId).ExecuteDeleteAsync();
        }
    }
}
