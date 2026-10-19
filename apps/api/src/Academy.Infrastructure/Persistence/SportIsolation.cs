using System.Linq.Expressions;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public static bool PlanInSports(Guid academy, Guid plan, Guid[] sports) => throw new InvalidOperationException("SQL query only");
    private void ConfigureSportIsolation(ModelBuilder b)
    {
        b.HasDbFunction(typeof(FoundationDbContext).GetMethod(nameof(PlanInSports))!).HasName("academy_plan_in_sports");
        // One selected membership supplies BOTH branches and sports. Never union
        // permissions from another role/membership into this request context.
        foreach (var type in b.Model.GetEntityTypes().Where(e => e.BaseType is null
            && e.FindProperty("AcademyId")?.ClrType == typeof(Guid)
            && e.FindProperty("SportId")?.ClrType == typeof(Guid)).Select(e => e.ClrType).ToArray())
        {
            var row = Expression.Parameter(type, "row");
            var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)],
                Expression.Property(Expression.Constant(this), nameof(RequestSportIds)), Expression.Property(row, "SportId"));
            AddSportFilter(b, type, Expression.Lambda(contains, row));
        }
        SportFilter<Sport>(b, x => RequestSportIds.Contains(x.Id));
        SportFilter<OperatingExpense>(b, x => x.SportId.HasValue && RequestSportIds.Contains(x.SportId.Value));
        // Shared discount definitions may be read for correct sibling pricing, not changed.
        SportFilter<DiscountRule>(b, x => !x.SportId.HasValue || RequestSportIds.Contains(x.SportId.Value));
        SportFilter<DiscountRuleVersion>(b, x => DiscountRules.Any(r => r.Id == x.DiscountRuleId));
        SportFilter<Player>(b, x => SportEnrollments.Any(e => e.PlayerId == x.Id));
        SportFilter<PlayerCard>(b, x => Players.Any(p => p.Id == x.PlayerId));
        SportFilter<GuardianPlayerLink>(b, x => Players.Any(p => p.Id == x.PlayerId));
        SportFilter<GuardianProfile>(b, x => GuardianPlayerLinks.Any(l => l.GuardianId == x.Id));
        SportFilter<RecurringSchedule>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        SportFilter<StaffGroupAssignment>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        SportFilter<PlayerAttendance>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        SportFilter<StaffAttendance>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        SportFilter<AttendanceCorrection>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        SportFilter<SubscriptionSessionMovement>(b, x => SubscriptionPeriods.Any(p => p.Id == x.SubscriptionPeriodId));
        SportFilter<SubscriptionAdjustment>(b, x => SubscriptionPeriods.Any(p => p.Id == x.SubscriptionPeriodId));
        // Plan's branch filter already queries its publications; use a tenant-
        // parameterized scalar lookup to avoid a cyclic EF filter expansion.
        SportFilter<PlanBranchPublication>(b, x => PlanInSports(x.AcademyId, x.SubscriptionPlanId, RequestSportIds));
        SportFilter<PlanPriceVersion>(b, x => SubscriptionPlans.Any(p => p.Id == x.SubscriptionPlanId));
        SportFilter<RenewalDiscountAdjustment>(b, x => RenewalRequests.Any(r => r.Id == x.RenewalRequestId));
        SportFilter<PaymentRequest>(b, x => RenewalRequests.Any(r => r.Id == x.RenewalRequestId));
        SportFilter<PaymentProviderEvent>(b, x => PaymentRequests.Any(p => p.Id == x.PaymentRequestId));
        SportFilter<PaymentCollection>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        SportFilter<Receipt>(b, x => Collections.Any(c => c.Id == x.CollectionId));
        SportFilter<CollectionRefund>(b, x => Collections.Any(c => c.Id == x.CollectionId));
        SportFilter<PaymentProof>(b, x => PaymentRequests.Any(p => p.Id == x.PaymentRequestId));
        SportFilter<BeneficiaryRenewalReference>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        SportFilter<FamilyRenewalLine>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        // Atomic household orders are visible only when EVERY line is in scope.
        // Avoid IgnoreQueryFilters inside a composed query: EF can widen its roots.
        SportFilter<FamilyRenewalRequest>(b, x => x.LineCount > 0 && FamilyRenewalLines.Count(l => l.RequestId == x.Id) == x.LineCount);
        SportFilter<ReceivableDocument>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        SportFilter<ReceivableMovement>(b, x => ReceivableDocuments.Any(d => d.Id == x.DocumentId));
        SportFilter<ReceivableCreditApplication>(b, x => ReceivableDocuments.Any(d => d.Id == x.SourceDocumentId) && ReceivableDocuments.Any(d => d.Id == x.TargetDocumentId));
        SportFilter<FreeTrainingApproval>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        SportFilter<OperatingExpenseAllocation>(b, x => OperatingExpenses.Any(e => e.Id == x.ExpenseId));
        SportFilter<ImportedEnrollment>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        SportFilter<ImportedHousehold>(b, x => GuardianProfiles.Any(g => g.Id == x.GuardianId));
        SportFilter<PlayerDiscountLink>(b, x => Players.Any(p => p.Id == x.PlayerId));
        SportFilter<CompensationMovement>(b, x => CompensationGrants.Any(g => g.Id == x.GrantId));
        SportFilter<FacilityResource>(b, x => FacilitySpaces.Any(s => s.Id == x.FacilitySpaceId));
        SportFilter<ResourceBooking>(b, x => FacilityResources.Any(r => r.Id == x.ResourceId));
        SportFilter<ResourceClosure>(b, x => FacilityResources.Any(r => r.Id == x.ResourceId));
        SportFilter<ResourceObservation>(b, x => FacilityResources.Any(r => r.Id == x.ResourceId));
        SportFilter<GroupResourceSchedule>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId) && FacilityResources.Any(r => r.Id == x.ResourceId));
        SportFilter<TrainingScheduleRevision>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        SportFilter<GroupPlacementRevision>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        SportFilter<AccessEntitlement>(b, x => ResourceBookings.Any(r => r.Id == x.ResourceBookingId));
        SportFilter<EntryVisit>(b, x => ResourceBookings.Any(r => r.Id == x.ResourceBookingId));
        SportFilter<DayPassTicket>(b, x => SubscriptionPlans.Any(p => p.Id == x.SubscriptionPlanId) && ResourceBookings.Any(r => r.Id == x.ResourceBookingId));
        SportFilter<AcademyMatch>(b, x => MatchTeams.Any(t => t.Id == x.TeamId));
        SportFilter<MatchTeamPlayer>(b, x => MatchTeams.Any(t => t.Id == x.TeamId));
        SportFilter<MatchParticipant>(b, x => AcademyMatches.Any(m => m.Id == x.MatchId));
        SportFilter<MatchCoachAssignment>(b, x => AcademyMatches.Any(m => m.Id == x.MatchId));
        SportFilter<OwnerFollowUp>(b, x => x.SourceKind=="Group"?TrainingGroups.Any(g=>g.Id==x.TrainingGroupId):x.SourceSportId.HasValue&&RequestSportIds.Contains(x.SourceSportId.Value));
        SportFilter<OwnerFollowUpChange>(b, x => OwnerFollowUps.Any(f => f.Id == x.FollowUpId));
        SportFilter<TournamentPayment>(b, x => SportEnrollments.Any(e => e.Id == x.EnrollmentId));
        // Child-global medical/media cannot be split reliably by sport. Narrow
        // sport administration does not delegate these shared sensitive records.
        SportFilter<PlayerMedicalRecord>(b, x => false);
        SportFilter<PlayerMedia>(b, x => false);
        SportFilter<PrivateAssetAudience>(b, x => Players.Any(p => p.Id == x.PlayerId));
        SportFilter<GroupMediaConsent>(b,x=>GuardianPlayerLinks.Any(l=>l.Id==x.GuardianPlayerLinkId));
        SportFilter<PrivateMediaConsentBasis>(b,x=>PrivateAssetAudiences.Any(a=>a.AssetId==x.AssetId&&a.PlayerId==x.PlayerId));
        SportFilter<PrivateMediaWithdrawal>(b,x=>PrivateAssets.Any(a=>a.Id==x.AssetId));
        SportFilter<PrivateMediaErasure>(b,x=>PrivateAssets.Any(a=>a.Id==x.AssetId));
        SportFilter<PrivateMediaPurge>(b,x=>PrivateAssets.Any(a=>a.Id==x.AssetId));
        SportFilter<PrivateMediaDownloadBudget>(b,x=>x.UserId==RequestUserId);
        SportFilter<PrivateAsset>(b, x => x.Purpose == "Campaign" || x.Purpose == "Photo" && x.PlayerId.HasValue && Players.Any(p => p.Id == x.PlayerId.Value)
            || x.Purpose == "Proof" && (PaymentProofs.Any(p => p.AssetId == x.Id) || FamilyRenewalRequests.Any(r => r.ProofAssetId == x.Id)
            || NewEnrollmentRequests.Any(r => r.ProofAssetId == x.Id) || ProductSales.Any(s => s.ProofAssetId == x.Id)
            || TournamentPayments.Any(p => p.ProofAssetId == x.Id) || DayPassTickets.Any(p => p.ProofAssetId == x.Id)));
        SportFilter<AcademyMembership>(b, x => x.Id == RequestMembershipId || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.Id));
        SportFilter<StaffDailyAttendance>(b, x => x.MembershipId == RequestMembershipId || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.MembershipId));
        SportFilter<StaffAttendanceChange>(b, x => StaffDailyAttendances.Any(a => a.Id == x.AttendanceId));
        SportFilter<StaffWorkPeriod>(b, x => StaffDailyAttendances.Any(a => a.Id == x.AttendanceId));
        SportFilter<StaffWorkSchedule>(b, x => x.MembershipId == RequestMembershipId || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.MembershipId));
    }
    private void SportFilter<T>(ModelBuilder b, Expression<Func<T, bool>> predicate) where T : class => AddSportFilter(b, typeof(T), predicate);
    private void AddSportFilter(ModelBuilder b, Type type, LambdaExpression predicate)
    {
        var restricted = Expression.AndAlso(Expression.Property(Expression.Constant(this), nameof(RequestScopeEnabled)), Expression.Property(Expression.Constant(this), nameof(SportScopeRestricted)));
        b.Entity(type).HasQueryFilter("trusted-sports", Expression.Lambda(Expression.OrElse(Expression.Not(restricted), predicate.Body), predicate.Parameters));
    }
    private async Task RevalidateSportAuthority(CancellationToken token)
    {
        // Same lock as owner grant edits. Held through persistence, including
        // commands which otherwise relied on EF's implicit SaveChanges transaction.
        await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{RequestAcademyId}:{RequestMembershipId}"}, 0))", token);
        var m = await AcademyMemberships.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == RequestAcademyId && x.Id == RequestMembershipId && x.UserId == RequestUserId && x.IsActive && x.User.IsActive && x.Academy.IsActive, token);
        if (m is null || m.Role != AcademyRole.AcademyAdmin || !m.UseActionGrants) throw new ResourceScopeException();
        if (m.PrimaryMembershipId is Guid primary)
        {
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{RequestAcademyId}:{primary}"}, 0))", token);
            if (!await AcademyMemberships.IgnoreQueryFilters().AnyAsync(p => p.Id == primary && p.AcademyId == RequestAcademyId && p.UserId == RequestUserId && p.PrimaryMembershipId == null && p.Role == AcademyRole.AcademyAdmin && p.IsActive, token)) throw new ResourceScopeException();
        }
        if (!m.AllSports)
        {
            var sports = await MembershipSportAccesses.IgnoreQueryFilters().Where(a => a.AcademyId == RequestAcademyId && a.MembershipId == m.Id && a.IsActive
                && Sports.IgnoreQueryFilters().Any(s => s.AcademyId == RequestAcademyId && s.Id == a.SportId && s.IsActive)).Select(a => a.SportId).ToArrayAsync(token);
            if (!SportScopeRestricted || RequestSportIds.Any(id => !sports.Contains(id))) throw new ResourceScopeException();
        }
        if (!m.AllBranches)
        {
            var branches = await MembershipBranchAccesses.IgnoreQueryFilters().Where(a => a.AcademyId == RequestAcademyId && a.MembershipId == m.Id && a.IsActive
                && Branches.IgnoreQueryFilters().Any(b => b.AcademyId == RequestAcademyId && b.Id == a.BranchId && b.IsActive)).Select(a => a.BranchId).ToArrayAsync(token);
            if (!BranchScopeRestricted || RequestBranchIds.Any(id => !branches.Contains(id))) throw new ResourceScopeException();
        }
        if (RequestModule is not null && !await StaffActionGrants.IgnoreQueryFilters().AnyAsync(g => g.AcademyId == RequestAcademyId && g.MembershipId == m.Id && g.Module == RequestModule && g.IsActive && g.Read
            && (RequestOperation == "Read" || RequestOperation == "Add" && g.Add || RequestOperation == "Edit" && g.Edit || RequestOperation == "Delete" && g.Delete), token)) throw new ResourceScopeException();
        if (RequestWorkflowKeys.Length > 0 && await StaffGrants.IgnoreQueryFilters().AnyAsync(g => g.AcademyId == RequestAcademyId && g.MembershipId == m.Id && g.IsActive && !g.Allowed && RequestWorkflowKeys.Contains(g.Permission),token)) throw new ResourceScopeException();
        foreach(var required in RequestWorkflowRequirements)
            if(await StaffGrants.IgnoreQueryFilters().AnyAsync(g=>g.AcademyId==RequestAcademyId&&g.MembershipId==m.Id&&g.IsActive&&g.Permission==required.Key,token)
                &&!await StaffActionGrants.IgnoreQueryFilters().AnyAsync(g=>g.AcademyId==RequestAcademyId&&g.MembershipId==m.Id&&g.Module==required.Module&&g.IsActive&&g.Read&&(required.Operation=="Read"||required.Operation=="Add"&&g.Add||required.Operation=="Edit"&&g.Edit||required.Operation=="Write"&&g.Add&&g.Edit),token))throw new ResourceScopeException();
    }
    private async Task GuardSportWrite(EntityEntry entry, EntityEntry[] entries, CancellationToken token)
    {
        foreach (var value in new[] { entry.Metadata.FindProperty("SportId") is not null ? entry.Property("SportId").CurrentValue : null,
            entry.State != EntityState.Added && entry.Metadata.FindProperty("SportId") is not null ? entry.Property("SportId").OriginalValue : null })
            if (value is Guid sport && !RequestSportIds.Contains(sport)) throw new ResourceScopeException();
        if (entry.Entity is Sport sportRow && !RequestSportIds.Contains(sportRow.Id)) throw new ResourceScopeException();
        // Shared academy-wide configuration/identities cannot be edited through a
        // narrow operational grant. The owner keeps the reviewed administration path.
        if (entry.Entity is Branch or AgeCategory or Facility or AcademyMembership or MembershipBranchAccess or MembershipSportAccess
            or StaffActionGrant or StaffGrant or StaffRoleTemplate or StaffRoleTemplateAudit or Operations.AcademyExperience or Operations.AcademyNews or NutritionItem
            || entry.Entity is DiscountRule rule && (!rule.SportId.HasValue || entry.State != EntityState.Added && entry.Property(nameof(DiscountRule.SportId)).OriginalValue is null)) throw new ResourceScopeException();
        if (entry.Metadata.FindProperty("SportId")?.ClrType == typeof(Guid?) && entry.Property("SportId").CurrentValue is null) throw new ResourceScopeException();
        Guid? player = entry.Entity is Player p ? p.Id : entry.Metadata.FindProperty("PlayerId") is not null ? entry.Property("PlayerId").CurrentValue as Guid? : null;
        if (player.HasValue && entry.Entity is not (SportEnrollment or NewEnrollmentRequest))
        {
            // A visible child may also train in another sport; touching the global
            // identity/media/guardian link must not change that other sport's record.
            if (await SportEnrollments.IgnoreQueryFilters().AnyAsync(e => e.AcademyId == RequestAcademyId && e.PlayerId == player && !RequestSportIds.Contains(e.SportId), token)) throw new ResourceScopeException();
        }
        if (entry.Entity is GuardianProfile guardian && await GuardianPlayerLinks.IgnoreQueryFilters().AnyAsync(l => l.AcademyId == RequestAcademyId && l.GuardianId == guardian.Id
            && SportEnrollments.IgnoreQueryFilters().Any(e => e.AcademyId == RequestAcademyId && e.PlayerId == l.PlayerId && !RequestSportIds.Contains(e.SportId)), token)) throw new ResourceScopeException();
        // Do not permit a tracked out-of-scope object to be changed by detaching its
        // relation. Check its original persisted visibility, in addition to current FKs.
        if (entry.State != EntityState.Added && entry.Metadata.FindProperty("Id") is not null)
        {
            var method = typeof(FoundationDbContext).GetMethod(nameof(Visible), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(entry.Metadata.ClrType);
            if (!await (Task<bool>)method.Invoke(this, [(Guid)entry.Property("Id").OriginalValue!, token])!) throw new ResourceScopeException();
        }
    }
}
