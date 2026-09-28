using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Tenancy.Academy> Academies => Set<Tenancy.Academy>();
    public DbSet<AcademyMembership> AcademyMemberships => Set<AcademyMembership>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<GuardianOtpChallenge> GuardianOtpChallenges => Set<GuardianOtpChallenge>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Sport> Sports => Set<Sport>();
    public DbSet<AgeCategory> AgeCategories => Set<AgeCategory>();
    public DbSet<TrainingGroup> TrainingGroups => Set<TrainingGroup>();
    public DbSet<RecurringSchedule> RecurringSchedules => Set<RecurringSchedule>();
    public DbSet<StaffGroupAssignment> StaffGroupAssignments => Set<StaffGroupAssignment>();
    public DbSet<GuardianProfile> GuardianProfiles => Set<GuardianProfile>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<GuardianPlayerLink> GuardianPlayerLinks => Set<GuardianPlayerLink>();
    public DbSet<SportEnrollment> SportEnrollments => Set<SportEnrollment>();
    public DbSet<NewEnrollmentRequest> NewEnrollmentRequests => Set<NewEnrollmentRequest>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<SubscriptionPeriod> SubscriptionPeriods => Set<SubscriptionPeriod>();
    public DbSet<RenewalRequest> RenewalRequests => Set<RenewalRequest>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<PaymentProviderEvent> PaymentProviderEvents => Set<PaymentProviderEvent>();
    public DbSet<PaymentCollection> Collections => Set<PaymentCollection>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<BeneficiaryRenewalReference> BeneficiaryRenewalReferences => Set<BeneficiaryRenewalReference>();
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<PlayerAttendance> PlayerAttendances => Set<PlayerAttendance>();
    public DbSet<StaffAttendance> StaffAttendances => Set<StaffAttendance>();
    public DbSet<SubscriptionSessionMovement> SubscriptionSessionMovements => Set<SubscriptionSessionMovement>();
    public DbSet<EvaluationCriterion> EvaluationCriteria => Set<EvaluationCriterion>();
    public DbSet<PlayerEvaluation> PlayerEvaluations => Set<PlayerEvaluation>();
    public DbSet<EvaluationScore> EvaluationScores => Set<EvaluationScore>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.HasIndex(x => x.PhoneNumber).IsUnique();
        });

        builder.Ignore<IdentityUserLogin<Guid>>();
        builder.Ignore<IdentityUserToken<Guid>>();

        builder.Entity<Tenancy.Academy>(entity =>
        {
            entity.ToTable("Academies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ArabicName).HasMaxLength(200);
            entity.Property(x => x.EnglishName).HasMaxLength(200);
            entity.Property(x => x.Slug).HasMaxLength(100);
            entity.Property(x => x.TimeZone).HasMaxLength(80);
            entity.Property(x => x.DefaultCurrency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<AcademyMembership>(entity =>
        {
            entity.ToTable("AcademyMemberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.AcademyId, x.UserId }).IsUnique();
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id });
            entity.HasIndex(x => new { x.UserId, x.IsActive });
            entity.HasOne(x => x.Academy).WithMany().HasForeignKey(x => x.AcademyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserSession>(entity =>
        {
            entity.ToTable("UserSessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(128);
            entity.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GuardianOtpChallenge>(entity =>
        {
            entity.ToTable("GuardianOtpChallenges");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PhoneNumberNormalized).HasMaxLength(32);
            entity.HasIndex(x => new { x.PhoneNumberNormalized, x.ExpiresAtUtc });
        });

        ConfigureTenantEntity<Branch>(builder, "Branches");
        builder.Entity<Branch>(entity =>
        {
            entity.Property(x => x.ArabicName).HasMaxLength(160);
            entity.Property(x => x.EnglishName).HasMaxLength(160);
            entity.Property(x => x.Address).HasMaxLength(300);
            entity.HasIndex(x => new { x.AcademyId, x.ArabicName }).IsUnique();
        });

        ConfigureTenantEntity<Sport>(builder, "Sports");
        builder.Entity<Sport>(entity =>
        {
            entity.Property(x => x.ArabicName).HasMaxLength(120);
            entity.Property(x => x.EnglishName).HasMaxLength(120);
            entity.HasIndex(x => new { x.AcademyId, x.ArabicName }).IsUnique();
        });

        ConfigureTenantEntity<AgeCategory>(builder, "AgeCategories");
        builder.Entity<AgeCategory>(entity =>
        {
            entity.Property(x => x.ArabicName).HasMaxLength(120);
            entity.HasIndex(x => new { x.AcademyId, x.ArabicName }).IsUnique();
        });

        ConfigureTenantEntity<TrainingGroup>(builder, "TrainingGroups");
        builder.Entity<TrainingGroup>(entity =>
        {
            entity.Property(x => x.ArabicName).HasMaxLength(180);
            entity.Property(x => x.EnglishName).HasMaxLength(180);
            entity.HasIndex(x => new { x.AcademyId, x.ArabicName }).IsUnique();
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId });
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AgeCategory).WithMany().HasForeignKey(x => new { x.AcademyId, x.AgeCategoryId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<RecurringSchedule>(builder, "RecurringSchedules");
        builder.Entity<RecurringSchedule>(entity =>
        {
            entity.HasIndex(x => new { x.AcademyId, x.TrainingGroupId, x.DayOfWeek, x.StartTime }).IsUnique();
            entity.HasOne(x => x.TrainingGroup).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureTenantEntity<StaffGroupAssignment>(builder, "StaffGroupAssignments");
        builder.Entity<StaffGroupAssignment>(entity =>
        {
            entity.HasAlternateKey(x => new { x.AcademyId, x.AcademyMembershipId, x.TrainingGroupId });
            entity.HasOne(x => x.AcademyMembership).WithMany().HasForeignKey(x => new { x.AcademyId, x.AcademyMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TrainingGroup).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<GuardianProfile>(builder, "GuardianProfiles");
        builder.Entity<GuardianProfile>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.Property(x => x.ContactPhone).HasMaxLength(32);
            entity.HasIndex(x => new { x.AcademyId, x.UserId }).IsUnique();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<Player>(builder, "Players");
        builder.Entity<Player>(entity =>
        {
            entity.Property(x => x.PlayerCode).HasMaxLength(24);
            entity.Property(x => x.ArabicName).HasMaxLength(180);
            entity.Property(x => x.EnglishName).HasMaxLength(180);
            entity.Property(x => x.HeightCm).HasPrecision(5, 2);
            entity.Property(x => x.WeightKg).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.AcademyId, x.PlayerCode }).IsUnique();
            entity.HasIndex(x => new { x.AcademyId, x.ArabicName });
        });

        ConfigureTenantEntity<GuardianPlayerLink>(builder, "GuardianPlayerLinks");
        builder.Entity<GuardianPlayerLink>(entity =>
        {
            entity.Property(x => x.RelationshipType).HasMaxLength(50);
            entity.HasIndex(x => new { x.AcademyId, x.GuardianId, x.PlayerId }).IsUnique();
            entity.HasOne(x => x.Guardian).WithMany().HasForeignKey(x => new { x.AcademyId, x.GuardianId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Player).WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<SportEnrollment>(builder, "SportEnrollments");
        builder.Entity<SportEnrollment>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.AcademyId, x.PlayerId, x.SportId, x.TrainingGroupId }).IsUnique();
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId });
            entity.HasOne(x => x.Player).WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TrainingGroup).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<NewEnrollmentRequest>(builder, "NewEnrollmentRequests");
        builder.Entity<NewEnrollmentRequest>(entity =>
        {
            entity.Property(x => x.RequestType).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.NewChildArabicName).HasMaxLength(180);
            entity.Property(x => x.Notes).HasMaxLength(600);
            entity.Property(x => x.AdminNotes).HasMaxLength(600);
            entity.Property(x => x.GuardianVisibleReason).HasMaxLength(400);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100);
            entity.HasIndex(x => new { x.AcademyId, x.RequestedByGuardianUserId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.AcademyId, x.Status, x.CreatedAtUtc });
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
            entity.HasOne(x => x.RequestedByGuardianUser).WithMany().HasForeignKey(x => x.RequestedByGuardianUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ExistingPlayer).WithMany().HasForeignKey(x => new { x.AcademyId, x.ExistingPlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PreferredBranch).WithMany().HasForeignKey(x => new { x.AcademyId, x.PreferredBranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ApprovedPlayer).WithMany().HasForeignKey(x => new { x.AcademyId, x.ApprovedPlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedSportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.CreatedSportEnrollmentId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_NewEnrollmentRequests_Shape", "(\"RequestType\" = 'ExistingChildNewSport' AND \"ExistingPlayerId\" IS NOT NULL AND \"NewChildArabicName\" IS NULL AND \"NewChildDateOfBirth\" IS NULL) OR (\"RequestType\" = 'NewChild' AND \"ExistingPlayerId\" IS NULL AND \"NewChildArabicName\" IS NOT NULL AND \"NewChildDateOfBirth\" IS NOT NULL)");
                t.HasCheckConstraint("CK_NewEnrollmentRequests_Review", "(\"Status\" IN ('Pending', 'UnderReview') AND \"ReviewedAtUtc\" IS NULL AND \"ReviewedByUserId\" IS NULL) OR (\"Status\" IN ('Approved', 'Rejected', 'Cancelled'))");
            });
        });

        ConfigureTenantEntity<SubscriptionPlan>(builder, "SubscriptionPlans");
        builder.Entity<SubscriptionPlan>(entity =>
        {
            entity.Property(x => x.ArabicName).HasMaxLength(160);
            entity.Property(x => x.EnglishName).HasMaxLength(160);
            entity.Property(x => x.PlanType).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
            entity.HasIndex(x => new { x.AcademyId, x.SportId, x.ArabicName }).IsUnique();
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_SubscriptionPlans_Configuration", "(\"PlanType\" = 'Duration' AND \"DurationDays\" > 0 AND \"SessionCount\" IS NULL) OR (\"PlanType\" = 'Sessions' AND \"DurationDays\" IS NULL AND \"SessionCount\" > 0) OR (\"PlanType\" = 'Combined' AND \"DurationDays\" > 0 AND \"SessionCount\" > 0)"));
        });

        ConfigureTenantEntity<BeneficiaryRenewalReference>(builder, "BeneficiaryRenewalReferences");
        builder.Entity<BeneficiaryRenewalReference>(entity =>
        {
            entity.Property(x => x.CodeHash).HasMaxLength(64).IsFixedLength();
            entity.Property(x => x.CodeHint).HasMaxLength(12);
            entity.HasIndex(x => new { x.AcademyId, x.CodeHash }).IsUnique();
            entity.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.RevokedAtUtc });
            entity.HasOne(x => x.SportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<SubscriptionPeriod>(builder, "SubscriptionPeriods");
        builder.Entity<SubscriptionPeriod>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.PriceSnapshot).HasPrecision(18, 2);
            entity.Property(x => x.CurrencySnapshot).HasMaxLength(3).IsFixedLength();
            entity.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.StartDate });
            entity.HasIndex(x => new { x.AcademyId, x.CollectionId }).IsUnique();
            entity.HasOne(x => x.SportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SubscriptionPlan).WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Collection).WithMany().HasForeignKey(x => new { x.AcademyId, x.CollectionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_SubscriptionPeriods_RemainingSessions", "\"RemainingSessions\" IS NULL OR \"RemainingSessions\" >= 0"));
        });

        ConfigureTenantEntity<RenewalRequest>(builder, "RenewalRequests");
        builder.Entity<RenewalRequest>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.AmountExpected).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100);
            entity.HasIndex(x => new { x.AcademyId, x.RequestedByUserId, x.IdempotencyKey }).IsUnique();
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportEnrollmentId });
            entity.HasOne(x => x.SportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SubscriptionPlan).WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<PaymentRequest>(builder, "PaymentRequests");
        builder.Entity<PaymentRequest>(entity =>
        {
            entity.Property(x => x.Provider).HasMaxLength(60); entity.Property(x => x.ProviderEnvironment).HasMaxLength(30);
            entity.Property(x => x.Amount).HasPrecision(18, 2); entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ProviderReference).HasMaxLength(100); entity.Property(x => x.CheckoutReference).HasMaxLength(160);
            entity.HasIndex(x => new { x.AcademyId, x.RenewalRequestId }).IsUnique();
            entity.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique();
            entity.HasOne(x => x.RenewalRequest).WithMany().HasForeignKey(x => new { x.AcademyId, x.RenewalRequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<PaymentProviderEvent>(builder, "PaymentProviderEvents");
        builder.Entity<PaymentProviderEvent>(entity =>
        {
            entity.Property(x => x.Provider).HasMaxLength(60); entity.Property(x => x.ProviderEventId).HasMaxLength(100);
            entity.Property(x => x.EventType).HasMaxLength(50); entity.Property(x => x.RawStatus).HasMaxLength(50);
            entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20); entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); entity.Property(x => x.ProcessingResult).HasMaxLength(100);
            entity.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique();
            entity.HasOne(x => x.PaymentRequest).WithMany().HasForeignKey(x => new { x.AcademyId, x.PaymentRequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<PaymentCollection>(builder, "Collections");
        builder.Entity<PaymentCollection>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 2); entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.PaymentMethod).HasMaxLength(60); entity.Property(x => x.Provider).HasMaxLength(60);
            entity.Property(x => x.ProviderReference).HasMaxLength(100); entity.Property(x => x.ConfirmedBy).HasMaxLength(60);
            entity.HasIndex(x => new { x.AcademyId, x.PaymentRequestId }).IsUnique();
            entity.HasOne(x => x.SportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RenewalRequest).WithMany().HasForeignKey(x => new { x.AcademyId, x.RenewalRequestId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportEnrollmentId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PaymentRequest).WithMany().HasForeignKey(x => new { x.AcademyId, x.PaymentRequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<Receipt>(builder, "Receipts");
        builder.Entity<Receipt>(entity =>
        {
            entity.Property(x => x.ReceiptNumber).HasMaxLength(40); entity.Property(x => x.PlayerNameSnapshot).HasMaxLength(180);
            entity.Property(x => x.SportNameSnapshot).HasMaxLength(120); entity.Property(x => x.PlanNameSnapshot).HasMaxLength(160);
            entity.Property(x => x.Amount).HasPrecision(18, 2); entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.PaymentMethod).HasMaxLength(60); entity.Property(x => x.ProviderReference).HasMaxLength(100);
            entity.HasIndex(x => new { x.AcademyId, x.CollectionId }).IsUnique();
            entity.HasIndex(x => new { x.AcademyId, x.ReceiptNumber }).IsUnique();
            entity.HasOne(x => x.Collection).WithMany().HasForeignKey(x => new { x.AcademyId, x.CollectionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<TrainingSession>(builder, "TrainingSessions");
        builder.Entity<TrainingSession>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId });
            entity.HasIndex(x => new { x.AcademyId, x.TrainingGroupId, x.SessionDate, x.StartTime }).IsUnique();
            entity.HasIndex(x => new { x.AcademyId, x.SessionDate, x.Status });
            entity.HasOne(x => x.TrainingGroup).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AgeCategory).WithMany().HasForeignKey(x => new { x.AcademyId, x.AgeCategoryId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RecurringSchedule).WithMany().HasForeignKey(x => new { x.AcademyId, x.RecurringScheduleId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_TrainingSessions_Time", "\"StartTime\" < \"EndTime\""));
        });

        ConfigureTenantEntity<PlayerAttendance>(builder, "PlayerAttendances");
        builder.Entity<PlayerAttendance>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => new { x.AcademyId, x.TrainingSessionId, x.SportEnrollmentId }).IsUnique();
            entity.HasOne(x => x.TrainingSession).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ConsumedSubscriptionPeriod).WithMany().HasForeignKey(x => new { x.AcademyId, x.ConsumedSubscriptionPeriodId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<StaffAttendance>(builder, "StaffAttendances");
        builder.Entity<StaffAttendance>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => new { x.AcademyId, x.TrainingSessionId, x.AcademyMembershipId }).IsUnique();
            entity.HasOne(x => x.TrainingSession).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AcademyMembership).WithMany().HasForeignKey(x => new { x.AcademyId, x.AcademyMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StaffGroupAssignment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AcademyMembershipId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.AcademyMembershipId, x.TrainingGroupId }).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureTenantEntity<SubscriptionSessionMovement>(builder, "SubscriptionSessionMovements");
        builder.Entity<SubscriptionSessionMovement>(entity =>
        {
            entity.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Reason).HasMaxLength(200);
            entity.HasIndex(x => new { x.AcademyId, x.PlayerAttendanceId, x.OccurredAtUtc });
            entity.HasIndex(x => new { x.AcademyId, x.ReversesMovementId }).IsUnique().HasFilter("\"ReversesMovementId\" IS NOT NULL");
            entity.HasOne(x => x.SubscriptionPeriod).WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPeriodId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerAttendance).WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerAttendanceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReversesMovement).WithMany().HasForeignKey(x => new { x.AcademyId, x.ReversesMovementId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_SubscriptionSessionMovements_Quantity", "(\"MovementType\" = 'AttendanceConsume' AND \"Quantity\" = -1 AND \"BalanceAfter\" = \"BalanceBefore\" - 1) OR (\"MovementType\" = 'AttendanceRestore' AND \"Quantity\" = 1 AND \"BalanceAfter\" = \"BalanceBefore\" + 1)"));
            entity.ToTable(t => t.HasCheckConstraint("CK_SubscriptionSessionMovements_Balance", "\"BalanceBefore\" >= 0 AND \"BalanceAfter\" >= 0"));
        });

        ConfigureTenantEntity<EvaluationCriterion>(builder, "EvaluationCriteria");
        builder.Entity<EvaluationCriterion>(entity =>
        {
            entity.Property(x => x.ArabicName).HasMaxLength(160);
            entity.Property(x => x.EnglishName).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(600);
            entity.Property(x => x.Weight).HasPrecision(8, 3);
            entity.Property(x => x.FootballAxis).HasConversion<string>().HasMaxLength(20);
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
            entity.HasIndex(x => new { x.AcademyId, x.SportId, x.ArabicName }).IsUnique();
            entity.HasIndex(x => new { x.AcademyId, x.SportId, x.DisplayOrder });
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_EvaluationCriteria_Weight", "\"Weight\" > 0"));
        });

        ConfigureTenantEntity<PlayerEvaluation>(builder, "PlayerEvaluations");
        builder.Entity<PlayerEvaluation>(entity =>
        {
            entity.Property(x => x.ReportingPeriod).HasMaxLength(120);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.GeneralNotes).HasMaxLength(2000);
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
            entity.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.EvaluationDate });
            entity.HasOne(x => x.SportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TrainingGroup).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.EvaluatedByUser).WithMany().HasForeignKey(x => x.EvaluatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PublishedByUser).WithMany().HasForeignKey(x => x.PublishedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReplacesEvaluation).WithMany().HasForeignKey(x => new { x.AcademyId, x.ReplacesEvaluationId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_PlayerEvaluations_Status", "\"Status\" IN ('Draft','Published','Superseded')"));
        });

        ConfigureTenantEntity<EvaluationScore>(builder, "EvaluationScores");
        builder.Entity<EvaluationScore>(entity =>
        {
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.CriterionNameSnapshot).HasMaxLength(160);
            entity.Property(x => x.WeightSnapshot).HasPrecision(8, 3);
            entity.Property(x => x.FootballAxisSnapshot).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.AcademyId, x.PlayerEvaluationId, x.EvaluationCriterionId }).IsUnique();
            entity.HasOne(x => x.PlayerEvaluation).WithMany(x => x.Scores).HasForeignKey(x => new { x.AcademyId, x.PlayerEvaluationId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.EvaluationCriterion).WithMany().HasForeignKey(x => new { x.AcademyId, x.EvaluationCriterionId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_EvaluationScores_Score", "\"Score\" IS NULL OR (\"Score\" >= 0 AND \"Score\" <= 100)"));
            entity.ToTable(t => t.HasCheckConstraint("CK_EvaluationScores_WeightSnapshot", "\"WeightSnapshot\" > 0"));
        });
    }

    private static void ConfigureTenantEntity<TEntity>(ModelBuilder builder, string table) where TEntity : TenantEntity
    {
        builder.Entity<TEntity>(entity =>
        {
            entity.ToTable(table);
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.AcademyId, x.Id });
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => new { x.AcademyId, x.IsActive });
            entity.HasOne<Tenancy.Academy>().WithMany().HasForeignKey(x => x.AcademyId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
