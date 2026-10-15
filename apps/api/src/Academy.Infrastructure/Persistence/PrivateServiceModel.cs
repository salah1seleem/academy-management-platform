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
    private static void ConfigurePrivateService(ModelBuilder b)
    {
        Tenant<PrivateServicePackage>(b); Tenant<PrivateServiceDelivery>(b);
        var p = b.Entity<PrivateServicePackage>();
        p.HasIndex(x => new { x.AcademyId, x.DocumentId }).IsUnique();
        p.Property(x => x.Currency).HasMaxLength(3); p.Property(x => x.TrainingFormat).HasMaxLength(20);
        p.HasOne<ReceivableDocument>().WithMany().HasForeignKey(x => new { x.AcademyId, x.DocumentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ReviewedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.ToTable(t => t.HasCheckConstraint("CK_PrivatePackage_Terms", "\"SessionCount\" BETWEEN 1 AND 1000 AND \"TrainingFormat\" IN ('Private','SemiPrivate')"));
        var d = b.Entity<PrivateServiceDelivery>();
        d.ToTable("PrivateServiceDeliveries");
        d.HasIndex(x => new { x.AcademyId, x.PackageId, x.Ordinal }).IsUnique();
        d.HasIndex(x => new { x.AcademyId, x.TrainingSessionId, x.SportEnrollmentId }).IsUnique();
        d.HasOne<PrivateServicePackage>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PackageId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<CoachCompensationAgreement>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AgreementId, x.EmployeeId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ApprovedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        d.ToTable(t => t.HasCheckConstraint("CK_PrivateDelivery_Ordinal", "\"Ordinal\" > 0 AND \"EndTime\" > \"StartTime\""));
    }
}
