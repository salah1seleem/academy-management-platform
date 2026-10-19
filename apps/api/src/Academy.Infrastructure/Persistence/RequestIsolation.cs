using System.Linq.Expressions;
using System.Reflection;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Finance;
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
    public bool SportScopeRestricted { get; private set; }
    public Guid[] RequestSportIds { get; private set; } = [];
    public string? RequestModule { get; private set; }
    public string RequestOperation { get; private set; } = "Read";
    public bool AdditionalContextScope { get; private set; }
    public void BindRequestScope(Guid academy, Guid user, Guid member, Guid[]? branches, Guid[]? sports = null, string? module = null, string operation = "Read", bool additionalContext = false)
    {
        if (RequestScopeEnabled) throw new InvalidOperationException("Request scope already bound");
        RequestAcademyId = academy; RequestUserId = user; RequestMembershipId = member;
        BranchScopeRestricted = branches is not null; RequestBranchIds = branches ?? [];
        SportScopeRestricted = sports is not null; RequestSportIds = sports ?? [];
        RequestModule = module; RequestOperation = operation;
        AdditionalContextScope = additionalContext;
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
        BranchFilter<SwimmingBranchPolicy>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<SportSettingsAudit>(b, x => !x.BranchId.HasValue || RequestBranchIds.Contains(x.BranchId.Value));
        BranchFilter<CashRegister>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CashierAssignment>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CashSession>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CashMovement>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CashReceiptPosting>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CashProductPosting>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<DayPassTicket>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<OperatingExpense>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<OperatingExpenseAllocation>(b, x => OperatingExpenses.Any(e => e.Id == x.ExpenseId));
        BranchFilter<CashRefundPosting>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CashReceiptVoid>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ProviderEventQuarantine>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<SettlementComparison>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<FreeTrainingApproval>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CashTransfer>(b, x => RequestBranchIds.Contains(x.BranchId) && RequestBranchIds.Contains(x.ToBranchId));
        BranchFilter<TrainingGroup>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<SportEnrollment>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<TrainingSession>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<Facility>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<FacilitySpace>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<FacilityResource>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ResourceBooking>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ResourceObservation>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<AccessEntitlement>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<EntryVisit>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<SessionCoachOverride>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<AttendanceCorrection>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ResourceClosure>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<GroupResourceSchedule>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<TrainingScheduleRevision>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<GroupPlacementRevision>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<EnrollmentGroupTransfer>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<EnrollmentSessionMove>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CompensationGrant>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CompensationBooking>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CompensationMovement>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<Employee>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<StaffAttendanceException>(b, x => RequestBranchIds.Contains(x.BranchId) && Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<MatchTeam>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ProductSale>(b, x => x.BranchId.HasValue && RequestBranchIds.Contains(x.BranchId.Value));
        BranchFilter<NewEnrollmentRequest>(b, x => RequestBranchIds.Contains(x.PreferredBranchId));
        BranchFilter<Player>(b, x => SportEnrollments.Any(e => e.PlayerId == x.Id));
        BranchFilter<PlayerCard>(b, x => Players.Any(p => p.Id == x.PlayerId));
        BranchFilter<GuardianPlayerLink>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<GuardianProfile>(b, x => GuardianPlayerLinks.Any(l => l.GuardianId == x.Id));
        BranchFilter<RecurringSchedule>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        BranchFilter<StaffGroupAssignment>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        BranchFilter<PlayerAttendance>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        BranchFilter<StaffAttendance>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        BranchFilter<SubscriptionSessionMovement>(b, x => SubscriptionPeriods.Any(p => p.Id == x.SubscriptionPeriodId));
        BranchFilter<SubscriptionPeriod>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<AcademyImportJob>(b, x => x.ActorUserId == RequestUserId);
        BranchFilter<ImportedEnrollment>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ImportedHousehold>(b, x => GuardianProfiles.Any(g => g.Id == x.GuardianId));
        BranchFilter<SubscriptionPlan>(b, x => x.AllBranches || PlanBranchPublications.Any(p => p.SubscriptionPlanId == x.Id && p.IsActive)
            || RenewalRequests.Any(r => r.SubscriptionPlanId == x.Id) || SubscriptionPeriods.Any(p => p.SubscriptionPlanId == x.Id));
        BranchFilter<PlanBranchPublication>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<PlanPriceVersion>(b, x => SubscriptionPlans.Any(p => p.Id == x.SubscriptionPlanId));
        BranchFilter<ReceivableDocument>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<ReceivableMovement>(b, x => ReceivableDocuments.Any(d => d.Id == x.DocumentId));
        BranchFilter<ReceivableCreditApplication>(b, x => ReceivableDocuments.Any(d => d.Id == x.SourceDocumentId) && ReceivableDocuments.Any(d => d.Id == x.TargetDocumentId));
        BranchFilter<SubscriptionAdjustment>(b, x => SubscriptionPeriods.Any(p => p.Id == x.SubscriptionPeriodId));
        BranchFilter<RenewalRequest>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<FamilyRenewalLine>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<FamilyRenewalRequest>(b, x => x.LineCount > 0 && FamilyRenewalLines.Count(l => l.RequestId == x.Id) == x.LineCount);
        BranchFilter<RenewalDiscountAdjustment>(b, x => RenewalRequests.Any(r => r.Id == x.RenewalRequestId));
        BranchFilter<PaymentRequest>(b, x => RenewalRequests.Any(r => r.Id == x.RenewalRequestId));
        BranchFilter<PaymentProviderEvent>(b, x => PaymentRequests.Any(p => p.Id == x.PaymentRequestId));
        BranchFilter<PaymentCollection>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<Receipt>(b, x => Collections.Any(c => c.Id == x.CollectionId));
        BranchFilter<BeneficiaryRenewalReference>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerEvaluation>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerDevelopmentGoal>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerGoalChange>(b, x => PlayerDevelopmentGoals.Any(g => g.Id == x.GoalId));
        BranchFilter<PlayerSkillPlan>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerSkillReview>(b, x => PlayerSkillPlans.Any(p => p.Id == x.PlanId));
        BranchFilter<PlayerLevelReview>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerAchievementCertificate>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerBadgeGrant>(b,x=>SportEnrollments.Any(e=>e.Id==x.SportEnrollmentId));
        BranchFilter<PlayerBadgeRevocation>(b,x=>SportEnrollments.Any(e=>e.Id==x.SportEnrollmentId));
        BranchFilter<PlayerCertificateWithdrawal>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerTechnicalReviewRequest>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<PlayerTechnicalReviewDecision>(b, x => SportEnrollments.Any(e => e.Id == x.SportEnrollmentId));
        BranchFilter<SessionLessonAssignment>(b, x => TrainingSessions.Any(s => s.Id == x.TrainingSessionId));
        BranchFilter<EvaluationScore>(b, x => PlayerEvaluations.Any(e => e.Id == x.PlayerEvaluationId));
        BranchFilter<PlayerMedicalRecord>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<PlayerMedia>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<PlayerDiscountLink>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<DiscountRule>(b, x => !x.BranchId.HasValue || RequestBranchIds.Contains(x.BranchId.Value));
        BranchFilter<DiscountRuleVersion>(b, x => DiscountRules.Any(r => r.Id == x.DiscountRuleId));
        BranchFilter<CollectionRefund>(b, x => Collections.Any(c => c.Id == x.CollectionId));
        BranchFilter<PaymentProof>(b, x => PaymentRequests.Any(p => p.Id == x.PaymentRequestId));
        BranchFilter<PrivateAssetAudience>(b, x => SportEnrollments.Any(e => e.PlayerId == x.PlayerId));
        BranchFilter<GroupMediaConsent>(b,x=>GuardianPlayerLinks.Any(l=>l.Id==x.GuardianPlayerLinkId));
        BranchFilter<PrivateMediaConsentBasis>(b,x=>PrivateAssetAudiences.Any(a=>a.AssetId==x.AssetId&&a.PlayerId==x.PlayerId));
        BranchFilter<PrivateMediaWithdrawal>(b,x=>PrivateAssets.Any(a=>a.Id==x.AssetId));
        BranchFilter<PrivateMediaErasure>(b,x=>PrivateAssets.Any(a=>a.Id==x.AssetId));
        BranchFilter<PrivateMediaPurge>(b,x=>PrivateAssets.Any(a=>a.Id==x.AssetId));
        BranchFilter<PrivateMediaDownloadBudget>(b,x=>x.UserId==RequestUserId);
        BranchFilter<PrivateAsset>(b, x => x.Purpose == "News" || x.UploadedByUserId == RequestUserId&&x.Purpose!="Media"
            || x.Purpose=="Campaign" && CommunityCampaignRevisions.Any(r=>r.ImageAssetId==x.Id&&CommunityCampaignTargets.Any(t=>t.RevisionId==r.Id&&RequestBranchIds.Contains(t.BranchId)))
            || x.PlayerId.HasValue && SportEnrollments.Any(e => e.PlayerId == x.PlayerId.Value)
            || x.TrainingGroupId.HasValue && TrainingGroups.Any(g => g.Id == x.TrainingGroupId.Value)
            || Employees.Any(e => e.PhotoAssetId == x.Id)
            || PaymentProofs.Any(p => p.AssetId == x.Id) || FamilyRenewalRequests.Any(r => r.ProofAssetId == x.Id) || NewEnrollmentRequests.Any(r => r.ProofAssetId == x.Id)
            || ProductSales.Any(s => s.ProofAssetId == x.Id) || TournamentPayments.Any(p => p.ProofAssetId == x.Id) || DayPassTickets.Any(p => p.ProofAssetId == x.Id));
        BranchFilter<AcademyMatch>(b, x => MatchTeams.Any(t => t.Id == x.TeamId));
        BranchFilter<MatchTeamPlayer>(b, x => MatchTeams.Any(t => t.Id == x.TeamId));
        BranchFilter<MatchParticipant>(b, x => AcademyMatches.Any(m => m.Id == x.MatchId));
        BranchFilter<MatchCoachAssignment>(b, x => AcademyMatches.Any(m => m.Id == x.MatchId));
        BranchFilter<OwnerFollowUp>(b, x => TrainingGroups.Any(g => g.Id == x.TrainingGroupId));
        BranchFilter<OwnerFollowUpChange>(b, x => OwnerFollowUps.Any(f => f.Id == x.FollowUpId));
        BranchFilter<CommunityCampaignTarget>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<CommunityCampaignReview>(b, x => !x.BranchId.HasValue || RequestBranchIds.Contains(x.BranchId.Value));
        BranchFilter<CommunityAdEvent>(b, x => RequestBranchIds.Contains(x.BranchId));
        BranchFilter<TournamentPayment>(b, x => SportEnrollments.Any(e => e.Id == x.EnrollmentId));
        BranchFilter<PayrollAdditionRequest>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<PayrollAdjustment>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<PayrollSettlement>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<SalaryVersion>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<ContractVersion>(b, x => Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<PayrollStatement>(b, x=>RequestBranchIds.Contains(x.BranchId)&&Employees.Any(e=>e.Id==x.EmployeeId));
        BranchFilter<PayrollDisbursement>(b, x=>PayrollStatements.Any(s=>s.Id==x.StatementId));
        BranchFilter<PayrollPayableCorrection>(b, x=>PayrollStatements.Any(s=>s.Id==x.StatementId));
        BranchFilter<PayrollAdvance>(b, x=>RequestBranchIds.Contains(x.BranchId)&&Employees.Any(e=>e.Id==x.EmployeeId));
        BranchFilter<CoachCompensationAgreement>(b, x => RequestBranchIds.Contains(x.BranchId) && Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<CoachCompensationApproval>(b, x => RequestBranchIds.Contains(x.BranchId) && Employees.Any(e => e.Id == x.EmployeeId));
        BranchFilter<PrivateServicePackage>(b, x => RequestBranchIds.Contains(x.BranchId) && ReceivableDocuments.Any(d => d.Id == x.DocumentId));
        BranchFilter<PrivateServiceDelivery>(b, x => RequestBranchIds.Contains(x.BranchId) && Employees.Any(e => e.Id == x.EmployeeId) && PrivateServicePackages.Any(p => p.Id == x.PackageId));
        BranchFilter<PrivateCompensationEntry>(b, x => RequestBranchIds.Contains(x.BranchId) && Employees.Any(e => e.Id == x.EmployeeId) && PrivateServiceDeliveries.Any(d => d.Id == x.DeliveryId));
        BranchFilter<AcademyMembership>(b, x => x.Id == RequestMembershipId || MembershipBranchAccesses.Any(a => a.MembershipId == x.Id && a.IsActive && RequestBranchIds.Contains(a.BranchId)) || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.Id) || Employees.Any(e => e.MembershipId == x.Id));
        BranchFilter<StaffDailyAttendance>(b, x => x.MembershipId == RequestMembershipId || Employees.Any(e => e.MembershipId == x.MembershipId) || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.MembershipId));
        BranchFilter<StaffAttendanceChange>(b, x => StaffDailyAttendances.Any(a => a.Id == x.AttendanceId));
        BranchFilter<StaffWorkPeriod>(b, x => StaffDailyAttendances.Any(a => a.Id == x.AttendanceId));
        b.Entity<StaffPasskey>().HasQueryFilter("own-staff-key",x=>!RequestScopeEnabled||x.MembershipId==RequestMembershipId&&x.UserId==RequestUserId);
        b.Entity<StaffPasskeyChallenge>().HasQueryFilter("own-staff-challenge",x=>!RequestScopeEnabled||x.MembershipId==RequestMembershipId&&x.UserId==RequestUserId);
        BranchFilter<StaffWorkSchedule>(b, x => x.MembershipId == RequestMembershipId || Employees.Any(e => e.MembershipId == x.MembershipId) || StaffGroupAssignments.Any(a => a.AcademyMembershipId == x.MembershipId));
        // Permissions and employee financial administration are not delegated by branch visibility.
        ConfigureSportIsolation(b);
    }
    private void BranchFilter<T>(ModelBuilder b, Expression<Func<T, bool>> predicate) where T : class
    {
        var restricted = Expression.AndAlso(Expression.Property(Expression.Constant(this), nameof(RequestScopeEnabled)), Expression.Property(Expression.Constant(this), nameof(BranchScopeRestricted)));
        b.Entity<T>().HasQueryFilter("trusted-branches", Expression.Lambda<Func<T, bool>>(Expression.OrElse(Expression.Not(restricted), predicate.Body), predicate.Parameters));
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        using var scopeTransaction = (SportScopeRestricted || AdditionalContextScope) && Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        GuardWrites(CancellationToken.None).GetAwaiter().GetResult();
        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        scopeTransaction?.Commit(); return saved;
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await using var scopeTransaction = (SportScopeRestricted || AdditionalContextScope) && Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        await GuardWrites(cancellationToken);
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (scopeTransaction is not null) await scopeTransaction.CommitAsync(cancellationToken);
        return saved;
    }
    private async Task GuardWrites(CancellationToken token)
    {
        ChangeTracker.DetectChanges();
        GuardPlayerGoalHistory();
        if(ChangeTracker.Entries().Any(e=>e.Entity is IBadgeRecord&&e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("قاعدة الشارة ومنحها وسحبها محفوظة؛ لا تكتب فوق التاريخ.");
        if(ChangeTracker.Entries().Any(e=>e.Entity is PlayerAchievementCertificate or PlayerCertificateWithdrawal && e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("الشهادة وسجل سحبها محفوظان؛ لا تكتب فوق المصدر.");
        GuardSessionLessons();
        if(ChangeTracker.Entries().Any(e=>e.Entity is GroupMediaConsent or PrivateMediaConsentBasis or PrivateMediaWithdrawal or PrivateMediaErasure or PrivateMediaPurge && e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("موافقة مشاركة الطفل ودليل النشر والسحب والحذف محفوظة؛ سجل قرارًا جديدًا بدل تعديل التاريخ.");
        if(ChangeTracker.Entries<PrivateAssetAudience>().Any(e=>e.OriginalValues.GetValue<Guid?>(nameof(PrivateAssetAudience.SportEnrollmentId))!=null&&e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("جمهور واقعة الوسائط محفوظ؛ لا تستبدله بأعضاء المجموعة الجدد.");
        foreach(var e in ChangeTracker.Entries<PrivateAsset>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.OriginalValues.GetValue<int>(nameof(PrivateAsset.MediaPolicyVersion))==1&&(e.State==EntityState.Deleted||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(PrivateAsset.IsActive) or nameof(PrivateAsset.UpdatedAtUtc)))))throw new InvalidOperationException("مصدر النشر وجمهوره ثابتان؛ ارفع نسخة مراجعة جديدة بدل الكتابة فوق الأصل.");
        if(ChangeTracker.Entries().Any(e=>e.Entity is PlayerTechnicalReviewRequest or PlayerTechnicalReviewDecision && e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("المراجعة الفنية محفوظة؛ لا تكتب فوق الطلب أو القرار السابق.");
        await GuardSkillHistory(token);
        if (ChangeTracker.Entries<PlayerLevelReview>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("مراجعات المستويات محفوظة؛ التصحيح بمراجعة جديدة.");
        foreach (var e in ChangeTracker.Entries<Academy.Infrastructure.Content.HelpVideoRevision>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            var original = e.OriginalValues.GetValue<string>(nameof(Academy.Infrastructure.Content.HelpVideoRevision.State));
            if (e.State == EntityState.Deleted || new[] { "Id", "LessonId", "Revision", "CreatedBy", "CreatedAtUtc" }.Any(p => e.Property(p).IsModified) ||
                original != "Draft" && (original != "Published" || e.Entity.State != "Archived" || e.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("State" or "EditVersion"))))
                throw new InvalidOperationException("إصدار الفيديو المنشور ثابت؛ أنشئ مسودة إصدار جديد أو أرشف المنشور.");
        }
        if(ChangeTracker.Entries<CommunityCampaignReview>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted)||ChangeTracker.Entries<CommunityAdEvent>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("دليل الموافقات والتفاعل محفوظ دون تعديل أو حذف.");
        foreach(var e in ChangeTracker.Entries<CommunityCampaignRevision>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(CommunityCampaignRevision.State) or nameof(CommunityCampaignRevision.UpdatedAtUtc))))throw new InvalidOperationException("لتغيير التصميم أنشئ نسخة جديدة تحتاج اعتمادًا جديدًا.");
        foreach(var e in ChangeTracker.Entries<CommunityCampaignTarget>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(CommunityCampaignTarget.Decision) or nameof(CommunityCampaignTarget.UpdatedAtUtc))))throw new InvalidOperationException("فروع النسخة ثابتة؛ سجل مراجعة أو نسخة جديدة.");
        foreach(var e in ChangeTracker.Entries<GuardianSponsorPreference>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(GuardianSponsorPreference.AdultDeclaredAtUtc) or nameof(GuardianSponsorPreference.InAppOptIn) or nameof(GuardianSponsorPreference.UpdatedAtUtc))))throw new InvalidOperationException("هوية صاحب الموافقة ثابتة.");
        if(ChangeTracker.Entries<FeedbackChange>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("سجل التحقيق محفوظ دون تعديل أو حذف.");
        if (ChangeTracker.Entries<GuardianFeedback>().Any(e => e.State == EntityState.Modified && e.Property(x => x.FollowUpPolicyEvidence).IsModified)) throw new InvalidOperationException("سياسة المتابعة وقت التقديم محفوظة دون إعادة كتابة.");
        if(ChangeTracker.Entries<GuardianFeedback>().Any(e=>e.State == EntityState.Modified && e.Properties.Any(p=>p.IsModified && p.Metadata.Name is nameof(GuardianFeedback.ReportedSubject) or nameof(GuardianFeedback.ReportedUrgency) or nameof(GuardianFeedback.IncidentOn) or nameof(GuardianFeedback.IncidentTrainingSessionId) or nameof(GuardianFeedback.IncidentSessionEvidence)))) throw new InvalidOperationException("تصنيف الأسرة وتاريخ ودليل الحصة الأصلي محفوظان؛ استخدم الفرز البشري دون إعادة كتابتهما.");
        if(ChangeTracker.Entries<OwnerFollowUpChange>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("سجل المتابعة محفوظ دون تعديل أو حذف.");
        foreach(var e in ChangeTracker.Entries<OwnerFollowUp>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(OwnerFollowUp.State) or nameof(OwnerFollowUp.AssignedToMembershipId) or nameof(OwnerFollowUp.DueOn) or nameof(OwnerFollowUp.Note) or nameof(OwnerFollowUp.ResolutionReason) or nameof(OwnerFollowUp.UpdatedAtUtc))))throw new InvalidOperationException("مصدر المتابعة ودليل إنشائها ثابتان.");
        if(ChangeTracker.Entries<MatchCoachAssignment>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("إسناد مدرب المباراة محفوظ؛ أضف مراجعة جديدة دون تعديل التاريخ أو حذفه.");
        GuardCashPaymentPurpose();
        foreach(var e in ChangeTracker.Entries<StaffPasskey>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||!e.OriginalValues.GetValue<bool>(nameof(StaffPasskey.IsActive))||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(StaffPasskey.SignCount) or nameof(StaffPasskey.BackedUp) or nameof(StaffPasskey.LastVerifiedAtUtc) or nameof(StaffPasskey.IsActive) or nameof(StaffPasskey.RevokedAtUtc) or nameof(StaffPasskey.UpdatedAtUtc))))throw new InvalidOperationException("هوية وسيلة التحقق ثابتة؛ أوقفها دون حذف الدليل أو تبديل المفتاح.");
        foreach(var e in ChangeTracker.Entries<StaffPasskeyChallenge>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||e.OriginalValues.GetValue<DateTimeOffset?>(nameof(StaffPasskeyChallenge.ConsumedAtUtc)).HasValue||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(StaffPasskeyChallenge.ConsumedAtUtc) or nameof(StaffPasskeyChallenge.VerifiedAtUtc) or nameof(StaffPasskeyChallenge.ResultJson) or nameof(StaffPasskeyChallenge.UpdatedAtUtc))))throw new InvalidOperationException("محاولة التحقق وأصلها ونتيجتها ثابتة وأحادية الاستخدام.");
        foreach (var e in ChangeTracker.Entries<StaffWorkPeriod>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted || new[]{nameof(StaffWorkPeriod.AcademyId),nameof(StaffWorkPeriod.AttendanceId),nameof(StaffWorkPeriod.MembershipId),nameof(StaffWorkPeriod.StartDate),nameof(StaffWorkPeriod.CreatedAtUtc),nameof(StaffWorkPeriod.IsActive)}.Any(p=>e.Property(p).IsModified)) throw new InvalidOperationException("هوية فترة العمل ثابتة؛ صحح التوقيت دون حذف الفترة أو نقلها.");
        if (ChangeTracker.Entries<StaffAttendanceChange>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("سجل تعديل الحضور ثابت ولا يعدل أو يحذف.");
        foreach (var e in ChangeTracker.Entries<StaffAttendanceException>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
            if (e.State == EntityState.Deleted || e.OriginalValues.GetValue<string>(nameof(StaffAttendanceException.Status)) != "Pending" || e.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(StaffAttendanceException.Status) or nameof(StaffAttendanceException.ReviewedByUserId) or nameof(StaffAttendanceException.ReviewedByMembershipId) or nameof(StaffAttendanceException.ReviewedAtUtc) or nameof(StaffAttendanceException.ReviewNote) or nameof(StaffAttendanceException.ReviewKey) or nameof(StaffAttendanceException.ReviewHash) or nameof(StaffAttendanceException.AttendanceId) or nameof(StaffAttendanceException.AppliedWorkPeriodId) or nameof(StaffAttendanceException.BeforeSnapshot) or nameof(StaffAttendanceException.AfterSnapshot) or nameof(StaffAttendanceException.UpdatedAtUtc))))
                throw new InvalidOperationException("طلب إثبات الحضور وأصله وقراره النهائي ثابتة ولا تحذف.");
        foreach(var e in ChangeTracker.Entries<AcademyMatch>().Where(e=>e.State==EntityState.Modified))
            if(new[]{nameof(AcademyMatch.CopiedFromMatchId),nameof(AcademyMatch.CopiedByMembershipId),nameof(AcademyMatch.CopyRequestKey),nameof(AcademyMatch.CopyRequestHash)}.Any(p=>e.Property(p).IsModified))throw new InvalidOperationException("مصدر نسخ المباراة ثابت؛ عدّل الموعد والمحتوى دون تغيير أصل النسخة.");
        if (ChangeTracker.Entries<PayrollStatement>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PayrollPayableCorrection>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PayrollAdvance>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PayrollDisbursement>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<SalaryVersion>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<ContractVersion>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PrivateCompensationEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PrivateServicePackage>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PrivateServiceDelivery>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<CoachCompensationAgreement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<CoachCompensationApproval>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("شروط اتفاق المدرب واعتمادها ثابتة؛ أنشئ نسخة جديدة بدل تعديل التاريخ.");
        var privateAdjustmentIds = ChangeTracker.Entries<PayrollAdjustment>().Where(e => e.State is EntityState.Modified or EntityState.Deleted).Select(e => e.Entity.Id).ToArray();
        var changedSalaries=ChangeTracker.Entries<Employee>().Where(e=>e.State==EntityState.Modified&&e.Property(x=>x.MonthlySalary).IsModified).Select(e=>e.Entity.Id).ToArray();
        if(changedSalaries.Length>0&&await SalaryVersions.AnyAsync(v=>changedSalaries.Contains(v.EmployeeId),token))throw new InvalidOperationException("الراتب المؤرخ ثابت؛ استخدم نسخة راتب جديدة.");
        if (privateAdjustmentIds.Length > 0 && await PrivateCompensationEntries.AnyAsync(e => e.PayrollAdjustmentId.HasValue && privateAdjustmentIds.Contains(e.PayrollAdjustmentId.Value), token)) throw new InvalidOperationException("قيد عمولة الخاص ثابت؛ التصحيح بحركة معتمدة مستقلة.");
        GuardCashFamilyPurpose();
        if (ChangeTracker.Entries<StaffRoleTemplateAudit>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("سجل قوالب الصلاحيات ثابت ولا يعدل أو يحذف.");
        if (ChangeTracker.Entries<SportSettingsAudit>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("سجل إعدادات الرياضة ثابت ولا يعدل أو يحذف.");
        if (ChangeTracker.Entries<ReceivableCreditApplication>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("تخصيص الرصيد الدائن ثابت ولا يعدل أو يحذف.");
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<FreeTrainingApproval>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("اعتماد الإعفاء ثابت؛ لا يعدل أو يحذف.");
        foreach (var entry in ChangeTracker.Entries<SubscriptionPeriod>().Where(e => e.State is EntityState.Modified or EntityState.Deleted && (e.Entity.Origin == SubscriptionOrigin.ApprovedWaiver || e.OriginalValues.GetValue<SubscriptionOrigin>(nameof(SubscriptionPeriod.Origin)) == SubscriptionOrigin.ApprovedWaiver)))
            if (entry.State == EntityState.Deleted || new[] { nameof(SubscriptionPeriod.Origin), nameof(SubscriptionPeriod.FreeTrainingApprovalId), nameof(SubscriptionPeriod.AcademyId), nameof(SubscriptionPeriod.SportEnrollmentId), nameof(SubscriptionPeriod.SportId), nameof(SubscriptionPeriod.SubscriptionPlanId), nameof(SubscriptionPeriod.CollectionId), nameof(SubscriptionPeriod.ImportJobId), nameof(SubscriptionPeriod.PriceSnapshot), nameof(SubscriptionPeriod.CurrencySnapshot), nameof(SubscriptionPeriod.CreatedByUserId) }.Any(p => entry.Property(p).IsModified)) throw new InvalidOperationException("مصدر الإعفاء ثابت ولا يتحول إلى قبض.");
        foreach (var entry in ChangeTracker.Entries<FamilyRenewalLine>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
            if (entry.State == EntityState.Deleted || entry.OriginalValues.GetValue<Guid?>(nameof(FamilyRenewalLine.ResultPaymentId)).HasValue || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(FamilyRenewalLine.ResultPaymentId) or nameof(FamilyRenewalLine.UpdatedAtUtc)))) throw new InvalidOperationException("أسعار وشروط سطور طلب الأسرة ثابتة ولا تعدل أو تحذف.");
        foreach (var entry in ChangeTracker.Entries<FamilyRenewalRequest>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
            if (entry.State == EntityState.Deleted || entry.OriginalValues.GetValue<FamilyRenewalStatus>(nameof(FamilyRenewalRequest.Status)) != FamilyRenewalStatus.Pending || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(FamilyRenewalRequest.Status) or nameof(FamilyRenewalRequest.Revision) or nameof(FamilyRenewalRequest.ProofAssetId) or nameof(FamilyRenewalRequest.DecidedByUserId) or nameof(FamilyRenewalRequest.DecidedAtUtc) or nameof(FamilyRenewalRequest.DecisionReason) or nameof(FamilyRenewalRequest.UpdatedAtUtc)))) throw new InvalidOperationException("طلب الأسرة ومصدره وقراره النهائي ثابتة ولا تحذف.");
        foreach (var entry in ChangeTracker.Entries<AttendanceCorrection>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if (entry.State==EntityState.Deleted || entry.OriginalValues.GetValue<AttendanceCorrectionState>(nameof(AttendanceCorrection.State)) is AttendanceCorrectionState.Approved or AttendanceCorrectionState.Rejected || entry.Properties.Any(p=>p.IsModified && p.Metadata.Name is not (nameof(AttendanceCorrection.State) or nameof(AttendanceCorrection.DecidedByUserId) or nameof(AttendanceCorrection.DecidedByMembershipId) or nameof(AttendanceCorrection.DecidedAtUtc) or nameof(AttendanceCorrection.DecisionReason) or nameof(AttendanceCorrection.AppliedAtUtc) or nameof(AttendanceCorrection.UpdatedAtUtc))))
                throw new InvalidOperationException("Attendance correction source and terminal decisions are immutable.");
        foreach (var entry in ChangeTracker.Entries<PlayerEvaluation>().Where(e=>e.State==EntityState.Modified))
            if(new[]{nameof(PlayerEvaluation.TrainingSessionId),nameof(PlayerEvaluation.SportEnrollmentId),nameof(PlayerEvaluation.TrainingGroupId),nameof(PlayerEvaluation.SportId),nameof(PlayerEvaluation.EvaluationDate),nameof(PlayerEvaluation.EvaluatedByUserId)}.Any(p=>entry.Property(p).IsModified)) throw new InvalidOperationException("سياق التقييم ثابت؛ لا ينقل التقييم إلى لاعب أو حصة أو مدرب آخر.");
        foreach (var entry in ChangeTracker.Entries<PlayerCard>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
            if (entry.State == EntityState.Deleted || !entry.OriginalValues.GetValue<bool>(nameof(PlayerCard.IsActive)) || entry.Entity.IsActive || new[] { nameof(PlayerCard.PlayerId), nameof(PlayerCard.AcademyId), nameof(PlayerCard.TokenHash), nameof(PlayerCard.ProtectedToken), nameof(PlayerCard.ExpiresAtUtc), nameof(PlayerCard.IssuedByUserId), nameof(PlayerCard.RequestKey), nameof(PlayerCard.PreviousVersion), nameof(PlayerCard.CreatedAtUtc) }.Any(p => entry.Property(p).IsModified))
                throw new InvalidOperationException("البطاقة ثابتة؛ يمكن إلغاؤها فقط ثم إصدار بطاقة جديدة.");
        foreach (var entry in ChangeTracker.Entries<EntryVisit>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
            if (entry.State == EntityState.Deleted || entry.OriginalValues.GetValue<DateTimeOffset?>(nameof(EntryVisit.ExitedAtUtc)).HasValue || !entry.Entity.ExitedAtUtc.HasValue || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(EntryVisit.ExitedAtUtc) or nameof(EntryVisit.ExitedByUserId) or nameof(EntryVisit.ExitKey) or nameof(EntryVisit.ExitHash) or nameof(EntryVisit.UpdatedAtUtc))))
                throw new InvalidOperationException("سجل الدخول ثابت؛ يسمح بتسجيل الخروج مرة واحدة فقط.");
        if (ChangeTracker.Entries().Any(e => e.Entity is SessionCoachOverride or AccessEntitlement or ResourceObservation or ReceivableDocument or ReceivableMovement or PlanPriceVersion or DiscountRuleVersion or ImportedEnrollment or ImportedHousehold or TrainingScheduleRevision or GroupPlacementRevision or EnrollmentGroupTransfer or EnrollmentSessionMove or CompensationGrant or CompensationMovement && e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("السجل المالي مثبت؛ التصحيح بحركة جديدة وليس بتعديل التاريخ أو حذفه.");
        if (ChangeTracker.Entries().Any(e => e.Entity is CashRegister or CashTransfer or CashMovement or CashReceiptPosting or CashProductPosting or CashRefundPosting or CashReceiptVoid or ProviderEventQuarantine or SettlementComparison && e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("سجل الصندوق مثبت ولا يعدل أو يحذف.");
        foreach(var entry in ChangeTracker.Entries<CashierAssignment>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(entry.State==EntityState.Deleted || entry.Properties.Any(p=>p.IsModified && p.Metadata.Name is not (nameof(CashierAssignment.IsActive) or nameof(CashierAssignment.UpdatedAtUtc))))
                throw new InvalidOperationException("تفويض أمين الصندوق لا ينقل؛ يلغى التفويض وينشأ تفويض مستقل.");
        foreach (var entry in ChangeTracker.Entries<CashSession>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if (entry.State == EntityState.Deleted || entry.OriginalValues.GetValue<DateTimeOffset?>(nameof(CashSession.ClosedAtUtc)).HasValue || !entry.Entity.ClosedAtUtc.HasValue || entry.Properties.Any(p=>p.IsModified && p.Metadata.Name is not (nameof(CashSession.ClosedAtUtc) or nameof(CashSession.ClosedByUserId) or nameof(CashSession.CountedAmount) or nameof(CashSession.ExpectedAmount) or nameof(CashSession.ClosingReason) or nameof(CashSession.CloseKey) or nameof(CashSession.CloseHash) or nameof(CashSession.UpdatedAtUtc))))
                throw new InvalidOperationException("وردية الصندوق ثابتة؛ يسمح بإقفالها مرة واحدة فقط.");
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is IPlanTermsSnapshot && e.State == EntityState.Modified))
            if (entry.Property(nameof(IPlanTermsSnapshot.PlanTypeSnapshot)).OriginalValue is not null && new[] { nameof(IPlanTermsSnapshot.PlanTypeSnapshot), nameof(IPlanTermsSnapshot.PlanNameSnapshot), nameof(IPlanTermsSnapshot.DurationDaysSnapshot), nameof(IPlanTermsSnapshot.SessionCountSnapshot), nameof(IPlanTermsSnapshot.PlanRevisionSnapshot), nameof(IPlanTermsSnapshot.DurationMonthsSnapshot), nameof(IPlanTermsSnapshot.ServiceKindSnapshot), nameof(IPlanTermsSnapshot.TrainingFormatSnapshot) }.Any(p => entry.Property(p).IsModified))
                throw new InvalidOperationException("شروط الاشتراك محفوظة عند الطلب ولا يمكن استبدالها بشروط باقة جديدة.");
        foreach (var entry in ChangeTracker.Entries<SubscriptionPeriod>().Where(e => e.State == EntityState.Modified && e.OriginalValues.GetValue<SubscriptionOrigin>(nameof(SubscriptionPeriod.Origin)) == SubscriptionOrigin.ImportedHistorical))
            if (new[] { nameof(SubscriptionPeriod.Origin), nameof(SubscriptionPeriod.ImportJobId), nameof(SubscriptionPeriod.CollectionId), nameof(SubscriptionPeriod.PriceSnapshot) }.Any(p => entry.Property(p).IsModified))
                throw new InvalidOperationException("اتفاق الاستيراد ثابت، ولا يتحول إلى تحصيل أو يعاد تسعيره تلقائيًا.");
        if (!RequestScopeEnabled) return;
        if (SportScopeRestricted || AdditionalContextScope) await RevalidateSportAuthority(token);
        ChangeTracker.DetectChanges();
        var entries = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && e.Metadata.FindProperty("AcademyId") is not null).ToArray();
        foreach (var entry in entries)
        {
            if (entry.Property("AcademyId").CurrentValue is not Guid academy || academy != RequestAcademyId) throw new ResourceScopeException();
            if (SportScopeRestricted) await GuardSportWrite(entry, entries, token);
            if (!BranchScopeRestricted && !SportScopeRestricted) continue;
            if (BranchScopeRestricted && entry.Entity is DiscountRule rule && (!rule.BranchId.HasValue || entry.State != EntityState.Added && entry.Property(nameof(DiscountRule.BranchId)).OriginalValue is null)) throw new ResourceScopeException();
            if (BranchScopeRestricted && entry.Entity is SubscriptionPlan plan)
            {
                if (plan.AllBranches || entry.State != EntityState.Added && (bool)entry.Property(nameof(SubscriptionPlan.AllBranches)).OriginalValue!) throw new ResourceScopeException();
                // Reading a shared product is not authority to change other branches' prices.
                if (entry.State != EntityState.Added && await PlanBranchPublications.IgnoreQueryFilters()
                    .AnyAsync(p => p.AcademyId == RequestAcademyId && p.SubscriptionPlanId == plan.Id && !RequestBranchIds.Contains(p.BranchId), token)) throw new ResourceScopeException();
            }
            if (BranchScopeRestricted && entry.Entity is Branch scopedBranch && !RequestBranchIds.Contains(scopedBranch.Id)) throw new ResourceScopeException();
            if (BranchScopeRestricted && entry.Entity is SkillCurriculum or SkillRequirement) throw new ResourceScopeException();
            if (BranchScopeRestricted && entry.Entity is TrainingExercise or SessionLessonTemplate or SessionLessonStep) throw new ResourceScopeException();
            foreach (var name in new[] { "BranchId", "PreferredBranchId" })
                if (BranchScopeRestricted && entry.Metadata.FindProperty(name) is not null && entry.Property(name).CurrentValue is Guid branch && !RequestBranchIds.Contains(branch)) throw new ResourceScopeException();
            foreach (var fk in entry.Metadata.GetForeignKeys().Where(f => f.PrincipalEntityType.FindProperty("AcademyId") is not null))
            {
                // The task's immutable historical creator may be outside the delegate's
                // staff-directory scope. Never widen identity reads; keep the existing
                // FK unchanged. Other task FKs and the actor/change row stay checked.
                if (entry.Entity is OwnerFollowUp && entry.State == EntityState.Modified &&
                    fk.Properties.Any(p => p.Name == nameof(OwnerFollowUp.CreatedByMembershipId)) &&
                    !entry.Property(nameof(OwnerFollowUp.CreatedByMembershipId)).IsModified) continue;
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
