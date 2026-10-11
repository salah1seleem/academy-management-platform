using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<AcademyImportJob> AcademyImportJobs => Set<AcademyImportJob>();
    public DbSet<ImportedEnrollment> ImportedEnrollments => Set<ImportedEnrollment>();
    public DbSet<ImportedHousehold> ImportedHouseholds => Set<ImportedHousehold>();
    private static void ConfigureImports(ModelBuilder b)
    {
        Tenant<AcademyImportJob>(b); Tenant<ImportedEnrollment>(b); Tenant<ImportedHousehold>(b);
        b.Entity<AcademyImportJob>(e => {
            e.Property(x => x.SourceSystem).HasMaxLength(80); e.Property(x => x.FileHash).HasMaxLength(64); e.Property(x => x.ReferenceStamp).HasMaxLength(64);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.ApprovedBranches).HasColumnType("jsonb");
            e.Property(x => x.GuardianResolutions).HasColumnType("jsonb");
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20); e.Property(x => x.ErrorCode).HasMaxLength(40);
            e.HasIndex(x => new { x.AcademyId, x.SourceSystem, x.FileHash }).IsUnique().HasFilter("\"State\" IN ('Queued', 'Running', 'Completed')");
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.IdempotencyKey }).IsUnique(); e.HasIndex(x => new { x.State, x.CreatedAtUtc });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_ImportJob_Counts", "\"RowCount\" BETWEEN 1 AND 1000 AND \"PublishedRows\" >= 0 AND \"ReplayedRows\" >= 0 AND \"PublishedRows\" + \"ReplayedRows\" <= \"RowCount\""); t.HasCheckConstraint("CK_ImportJob_Approval", "\"GuardianLinksApproved\" AND (\"State\" <> 'Completed' OR (\"StagedFile\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"PublishedRows\" + \"ReplayedRows\" = \"RowCount\"))"); });
        });
        b.Entity<ImportedEnrollment>(e => {
            e.Property(x => x.SourceSystem).HasMaxLength(80); e.Property(x => x.LegacyPlayerId).HasMaxLength(80); e.Property(x => x.LegacyEnrollmentId).HasMaxLength(80); e.Property(x => x.RowHash).HasMaxLength(64); e.Property(x => x.PlayerIdentityHash).HasMaxLength(64);
            e.Property(x => x.AgreementSnapshot).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AcademyId, x.SourceSystem, x.LegacyEnrollmentId }).IsUnique(); e.HasIndex(x => new { x.AcademyId, x.SourceSystem, x.LegacyPlayerId });
            e.HasOne<AcademyImportJob>().WithMany().HasForeignKey(x => new { x.AcademyId, x.JobId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GuardianProfile>().WithMany().HasForeignKey(x => new { x.AcademyId, x.GuardianId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPeriodId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ImportedHousehold>(e => {
            e.Property(x => x.SourceSystem).HasMaxLength(80); e.Property(x => x.HouseholdKey).HasMaxLength(84); e.Property(x => x.IdentityHash).HasMaxLength(64);
            e.HasIndex(x => new { x.AcademyId, x.SourceSystem, x.HouseholdKey }).IsUnique();
            e.HasOne<AcademyImportJob>().WithMany().HasForeignKey(x => new { x.AcademyId, x.JobId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GuardianProfile>().WithMany().HasForeignKey(x => new { x.AcademyId, x.GuardianId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SubscriptionPeriod>().Property(x => x.Origin).HasConversion<string>().HasMaxLength(24).HasDefaultValue(SubscriptionOrigin.Collected);
        b.Entity<SubscriptionPeriod>().HasOne<AcademyImportJob>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ImportJobId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SubscriptionPeriod>().ToTable(t => t.HasCheckConstraint("CK_SubscriptionPeriod_Origin", "(\"Origin\" = 'Collected' AND \"CollectionId\" IS NOT NULL AND \"ImportJobId\" IS NULL AND \"PriceSnapshot\" IS NOT NULL AND \"PriceSnapshot\" >= 0) OR (\"Origin\" = 'ImportedHistorical' AND \"CollectionId\" IS NULL AND \"ImportJobId\" IS NOT NULL AND (\"PriceSnapshot\" IS NULL OR \"PriceSnapshot\" >= 0))"));
        b.Entity<ReceivableDocument>().HasOne<AcademyImportJob>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ImportJobId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
