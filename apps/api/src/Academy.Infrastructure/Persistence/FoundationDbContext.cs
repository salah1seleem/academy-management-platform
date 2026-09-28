using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
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
            entity.HasIndex(x => new { x.AcademyId, x.AcademyMembershipId, x.TrainingGroupId }).IsUnique();
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
            entity.HasOne(x => x.Player).WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Sport).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TrainingGroup).WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
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
