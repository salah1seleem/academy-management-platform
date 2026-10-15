using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<PrivateServicePackage> PrivateServicePackages => Set<PrivateServicePackage>();
    public DbSet<PrivateServiceDelivery> PrivateServiceDeliveries => Set<PrivateServiceDelivery>();
    public DbSet<PrivateCompensationEntry> PrivateCompensationEntries => Set<PrivateCompensationEntry>();
    private static void ConfigurePrivateService(ModelBuilder b)
    {
        Tenant<PrivateServicePackage>(b); Tenant<PrivateServiceDelivery>(b);
        Tenant<PrivateCompensationEntry>(b);
        var p = b.Entity<PrivateServicePackage>();
        p.HasIndex(x => new { x.AcademyId, x.DocumentId }).IsUnique();
        p.Property(x => x.Currency).HasMaxLength(3); p.Property(x => x.TrainingFormat).HasMaxLength(20);
        p.HasOne<ReceivableDocument>().WithMany().HasForeignKey(x => new { x.AcademyId, x.DocumentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ReviewedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.ToTable(t => t.HasCheckConstraint("CK_PrivatePackage_Terms", "\"SessionCount\" BETWEEN 1 AND 1000 AND \"TrainingFormat\" IN ('Private','SemiPrivate')"));
        var d = b.Entity<PrivateServiceDelivery>();
        d.ToTable("PrivateServiceDeliveries");
        d.HasAlternateKey(x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.SportId });
        d.HasIndex(x => new { x.AcademyId, x.PackageId, x.Ordinal }).IsUnique();
        d.HasIndex(x => new { x.AcademyId, x.TrainingSessionId, x.SportEnrollmentId }).IsUnique();
        d.HasOne<PrivateServicePackage>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PackageId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<CoachCompensationAgreement>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AgreementId, x.EmployeeId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ApprovedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.ToTable(t => t.HasCheckConstraint("CK_PrivateDelivery_Ordinal", "\"Ordinal\" > 0 AND \"EndTime\" > \"StartTime\""));
        var c = b.Entity<PrivateCompensationEntry>();
        c.ToTable("PrivateCompensationEntries");
        c.HasOne<PrivateServiceDelivery>().WithMany().HasForeignKey(x => new { x.AcademyId, x.DeliveryId, x.EmployeeId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<PayrollAdjustment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PayrollAdjustmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<PrivateCompensationEntry>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ReviewEntryId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ActorMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x => new { x.AcademyId, x.DeliveryId, x.Component, x.Revision }).IsUnique();
        c.HasIndex(x => new { x.AcademyId, x.ActorMembershipId, x.RequestKey }).IsUnique();
        c.HasIndex(x => new { x.AcademyId, x.PayrollAdjustmentId }).IsUnique();
        c.HasIndex(x => new { x.AcademyId, x.ReviewEntryId }).IsUnique();
        c.Property(x => x.SourceSnapshot).HasColumnType("jsonb");
        c.Property(x => x.Kind).HasMaxLength(20); c.Property(x => x.Component).HasMaxLength(24); c.Property(x => x.Currency).HasMaxLength(3); c.Property(x => x.RequestKey).HasMaxLength(100);
        foreach (var hash in new[] { nameof(PrivateCompensationEntry.FactsHash), nameof(PrivateCompensationEntry.QuoteHash), nameof(PrivateCompensationEntry.RequestHash) }) c.Property<string>(hash).HasMaxLength(64);
        c.Property(x => x.Delta).HasPrecision(18, 2); c.Property(x => x.TargetAmount).HasPrecision(18, 2); c.Property(x => x.PostedBefore).HasPrecision(18, 2);
        c.ToTable(t => t.HasCheckConstraint("CK_PrivateEntry_Amounts", "\"Component\"='PrivateVariable' AND \"Revision\">0 AND \"PostedBefore\">=0 AND (\"TargetAmount\" IS NULL OR \"TargetAmount\">=0) AND ((\"Kind\"='Review' AND \"Delta\"=0 AND \"PayrollAdjustmentId\" IS NULL AND \"ReviewEntryId\" IS NULL) OR (\"Kind\"='Accrual' AND \"Delta\">0 AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NULL) OR (((\"Kind\"='Reversal' AND \"Delta\"<0) OR (\"Kind\"='Correction' AND \"Delta\"<>0)) AND \"TargetAmount\" IS NOT NULL AND \"Delta\"=\"TargetAmount\"-\"PostedBefore\" AND \"PayrollAdjustmentId\" IS NOT NULL AND \"ReviewEntryId\" IS NOT NULL))"));
    }
}
