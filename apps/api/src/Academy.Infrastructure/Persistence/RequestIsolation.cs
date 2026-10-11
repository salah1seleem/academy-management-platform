using System.Linq.Expressions;
using System.Reflection;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed class ResourceScopeException : Exception;

public sealed partial class FoundationDbContext
{
    // These values belong to one scoped DbContext, never pooled database session state.
    // HTTP middleware derives them only from the authenticated selected membership.
    public bool RequestScopeEnabled { get; private set; }
    public Guid RequestAcademyId { get; private set; }
    public Guid RequestUserId { get; private set; }
    public Guid RequestMembershipId { get; private set; }
    public bool BranchScopeRestricted { get; private set; }
    public Guid[] RequestBranchIds { get; private set; } = [];
    public void BindRequestScope(Guid academy, Guid user, Guid member, Guid[]? branches)
    {
        if (RequestScopeEnabled) throw new InvalidOperationException("Request scope already bound");
        RequestAcademyId = academy; RequestUserId = user; RequestMembershipId = member;
        BranchScopeRestricted = branches is not null; RequestBranchIds = branches ?? [];
        RequestScopeEnabled = true;
    }
    private void ConfigureRequestIsolation(ModelBuilder b)
    {
        foreach (var type in b.Model.GetEntityTypes().Where(e => e.BaseType is null && e.FindProperty("AcademyId")?.ClrType == typeof(Guid)).Select(e => e.ClrType).ToArray())
        {
            var row = Expression.Parameter(type, "row");
            var predicate = Expression.OrElse(Expression.Not(Expression.Property(Expression.Constant(this), nameof(RequestScopeEnabled))), Expression.Equal(Expression.Property(row, "AcademyId"), Expression.Property(Expression.Constant(this), nameof(RequestAcademyId))));
            b.Entity(type).HasQueryFilter("trusted-academy", Expression.Lambda(predicate, row));
        }
        BranchFilter<Branch>(b, x => RequestBranchIds.Contains(x.Id));
        BranchFilter<TrainingGroup>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<SportEnrollment>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<TrainingSession>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<Employee>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<MatchTeam>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ProductSale>(b, x => x.BranchId.HasValue && RequestBranchIds.Contains(x.BranchId.Value));
        BranchFilter<NewEnrollmentRequest>(b, x => RequestBranchIds.Contains(x.PreferredBranchId));
        BranchFilter<Player>(b, x => SportEnrollments.Any(e => e.PlayerId == x.Id));
        BranchFilter<GuardianPlayerLink>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<GuardianProfile>(b, x => GuardianPlayerLinks.Any(l => l.GuardianId == x.Id));
        BranchFilter<RecurringSchedule>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        BranchFilter<StaffGroupAssignment>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        BranchFilter<PlayerAttendance>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        BranchFilter<StaffAttendance>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        BranchFilter<SubscriptionSessionMovement>(b, x => SubscriptionPeriods.Any(p => p.Id == x.SubscriptionPeriodId));
        BranchFilter<SubscriptionPeriod>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<SubscriptionPlan>(b, x => x.AllBranches || PlanBranchPublications.Any(p => p.SubscriptionPlanId == x.Id && p.IsActive)
            || RenewalRequests.Any(r => r.SubscriptionPlanId == x.Id) || SubscriptionPeriods.Any(p => p.SubscriptionPlanId == x.Id));
        BranchFilter<PlanBranchPublication>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<PlanPriceVersion>(b, x => SubscriptionPlans.Any(p => p.Id == x.SubscriptionPlanId));
        BranchFilter<ReceivableDocument>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ReceivableMovement>(b, x => ReceivableDocuments.Any(d => d.Id == x.DocumentId));
        BranchFilter<SubscriptionAdjustment>(b, x => SubscriptionPeriods.Any(p => p.Id == x.SubscriptionPeriodId));
        BranchFilter<RenewalRequest>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<RenewalDiscountAdjustment>(b, x => RenewalRequests.Any(r => r.Id == x.RenewalRequestId));
        BranchFilter<PaymentRequest>(b, x => RenewalRequests.Any(r => r.Id == x.RenewalRequestId));
        BranchFilter<PaymentProviderEvent>(b, x => PaymentRequests.Any(p => p.Id == x.PaymentRequestId));
        BranchFilter<PaymentCollection>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<Receipt>(b, x => Collections.Any(c => c.Id == x.CollectionId));
        BranchFilter<BeneficiaryRenewalReference>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerEvaluation>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<EvaluationScore>(b, x => PlayerEvaluations.Any(e => e.Id == x.PlayerEvaluationId));
        BranchFilter<PlayerMedicalRecord>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<PlayerMedia>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<PlayerDiscountLink>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<DiscountRule>(b, x => !x.BranchId.HasValue || RequestBranchIds.Contains(x.BranchId.Value));
        BranchFilter<DiscountRuleVersion>(b, x => DiscountRules.Any(r => r.Id == x.DiscountRuleId));
        BranchFilter<CollectionRefund>(b, x => Collections.Any(c => c.Id == x.CollectionId));
        BranchFilter<PaymentProof>(b, x => PaymentRequests.Any(p => p.Id == x.PaymentRequestId));
        BranchFilter<PrivateAssetAudience>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<PrivateAsset>(b, x => x.Purpose == "News" || x.UploadedByUserId == RequestUserId
            || x.PlayerId.HasValue && SportEnrollments.Any(e => e.PlayerId == x.PlayerId.Value)
            || x.TrainingGroupId.HasValue && TrainingGroups.Any(g => g.Id == x.TrainingGroupId.Value)
            || Employees.Any(e => e.PhotoAssetId == x.Id)
            || PaymentProofs.Any(p => p.AssetId == x.Id) || NewEnrollmentRequests.Any(r => r.ProofAssetId == x.Id)
            || ProductSales.Any(s => s.ProofAssetId == x.Id) || TournamentPayments.Any(p => p.ProofAssetId == x.Id));
        BranchFilter<AcademyMatch>(b, x => MatchTeams.Any(t => t.Id == x.TeamId));
        BranchFilter<MatchTeamPlayer>(b, x => MatchTeams.Any(t => t.Id == x.TeamId));
        BranchFilter<MatchParticipant>(b, x => AcademyMatches.Any(m => m.Id == x.MatchId));
        BranchFilter<TournamentPayment>(b, x => SportEnrollments.Any(e => e.Id == x.EnrollmentId));
        BranchFilter<PayrollAdditionRequest>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<PayrollAdjustment>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<PayrollSettlement>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<AcademyMembership>(b, x => x.Id == RequestMembershipId || MembershipBranchAccesses.Any(a => a.MembershipId == x.Id && a.IsActive && RequestBranchIds.Contains(a.BranchId)) || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.Id) || Employees.Any(e => e.MembershipId == x.Id));
        BranchFilter<StaffDailyAttendance>(b, x => x.MembershipId == RequestMembershipId || Employees.Any(e => e.MembershipId == x.MembershipId) || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.MembershipId));
        BranchFilter<StaffWorkSchedule>(b, x => x.MembershipId == RequestMembershipId || Employees.Any(e => e.MembershipId == x.MembershipId) || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.MembershipId));
        // Permissions and employee financial administration are not delegated by branch visibility.
    }
    private void BranchFilter<T>(ModelBuilder b, Expression<Func<T, bool>> predicate) where T : class
    {
        var restricted = Expression.AndAlso(Expression.Property(Expression.Constant(this), nameof(RequestScopeEnabled)), Expression.Property(Expression.Constant(this), nameof(BranchScopeRestricted)));
        b.Entity<T>().HasQueryFilter("trusted-branches", Expression.Lambda<Func<T, bool>>(Expression.OrElse(Expression.Not(restricted), predicate.Body), predicate.Parameters));
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardWrites(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await GuardWrites(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private async Task GuardWrites(CancellationToken token)
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries().Any(e => e.Entity is ReceivableDocument or ReceivableMovement or PlanPriceVersion or DiscountRuleVersion && e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("السجل المالي مثبت؛ التصحيح بحركة جديدة وليس بتعديل التاريخ أو حذفه.");
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is IPlanTermsSnapshot && e.State == EntityState.Modified))
            if (entry.Property(nameof(IPlanTermsSnapshot.PlanTypeSnapshot)).OriginalValue is not null && new[] { nameof(IPlanTermsSnapshot.PlanTypeSnapshot), nameof(IPlanTermsSnapshot.PlanNameSnapshot), nameof(IPlanTermsSnapshot.DurationDaysSnapshot), nameof(IPlanTermsSnapshot.SessionCountSnapshot), nameof(IPlanTermsSnapshot.PlanRevisionSnapshot), nameof(IPlanTermsSnapshot.DurationMonthsSnapshot), nameof(IPlanTermsSnapshot.ServiceKindSnapshot), nameof(IPlanTermsSnapshot.TrainingFormatSnapshot) }.Any(p => entry.Property(p).IsModified))
                throw new InvalidOperationException("شروط الاشتراك محفوظة عند الطلب ولا يمكن استبدالها بشروط باقة جديدة.");
        if (!RequestScopeEnabled) return;
        ChangeTracker.DetectChanges();
        var entries = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && e.Metadata.FindProperty("AcademyId") is not null).ToArray();
        foreach (var entry in entries)
        {
            if (entry.Property("AcademyId").CurrentValue is not Guid academy || academy != RequestAcademyId) throw new ResourceScopeException();
            if (!BranchScopeRestricted) continue;
            if (entry.Entity is DiscountRule rule && (!rule.BranchId.HasValue || entry.State != EntityState.Added && entry.Property(nameof(DiscountRule.BranchId)).OriginalValue is null)) throw new ResourceScopeException();
            if (entry.Entity is SubscriptionPlan plan)
            {
                if (plan.AllBranches || entry.State != EntityState.Added && (bool)entry.Property(nameof(SubscriptionPlan.AllBranches)).OriginalValue!) throw new ResourceScopeException();
                // Reading a shared product is not authority to change other branches' prices.
                if (entry.State != EntityState.Added && await PlanBranchPublications.IgnoreQueryFilters()
                    .AnyAsync(p => p.AcademyId == RequestAcademyId && p.SubscriptionPlanId == plan.Id && !RequestBranchIds.Contains(p.BranchId), token)) throw new ResourceScopeException();
            }
            if (entry.Entity is Branch scopedBranch && !RequestBranchIds.Contains(scopedBranch.Id)) throw new ResourceScopeException();
            foreach (var name in new[] { "BranchId", "PreferredBranchId" })
                if (entry.Metadata.FindProperty(name) is not null && entry.Property(name).CurrentValue is Guid branch && !RequestBranchIds.Contains(branch)) throw new ResourceScopeException();
            foreach (var fk in entry.Metadata.GetForeignKeys().Where(f => f.PrincipalEntityType.FindProperty("AcademyId") is not null))
            {
                var key = fk.PrincipalKey.Properties.Select((p, i) => (p.Name, Value: entry.Property(fk.Properties[i].Name).CurrentValue)).SingleOrDefault(x => x.Name == "Id");
                if (key.Value is not Guid id) continue;
                var type = fk.PrincipalEntityType.ClrType;
                if (entries.Any(e => e.Metadata.ClrType == type && e.State == EntityState.Added && Equals(e.Property("Id").CurrentValue, id))) continue;
                var method = typeof(FoundationDbContext).GetMethod(nameof(Visible), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(type);
                if (!await (Task<bool>)method.Invoke(this, [id, token])!) throw new ResourceScopeException();
            }
        }
    }
    private Task<bool> Visible<T>(Guid id, CancellationToken token) where T : class => Set<T>().AsNoTracking().AnyAsync(e => EF.Property<Guid>(e, "Id") == id, token);
}
