using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<FamilyRenewalRequest> FamilyRenewalRequests => Set<FamilyRenewalRequest>();
    public DbSet<FamilyRenewalLine> FamilyRenewalLines => Set<FamilyRenewalLine>();
    private static void ConfigureFamilyRenewals(ModelBuilder b)
    {
        ConfigureTenantEntity<FamilyRenewalRequest>(b, "FamilyRenewalRequests");
        b.Entity<FamilyRenewalRequest>(e => {
            e.Property(x => x.IdempotencyKey).HasMaxLength(80);
            e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.QuoteHash).HasMaxLength(64);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DecisionReason).HasMaxLength(500);
            e.Property(x => x.OriginalAmount).HasPrecision(18, 2); e.Property(x => x.DiscountAmount).HasPrecision(18, 2); e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.AcademyId, x.RequestedByUserId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.AcademyId, x.ProofAssetId }).IsUnique();
            e.HasOne(x => x.Guardian).WithMany().HasForeignKey(x => new { x.AcademyId, x.GuardianId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ProofAssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_FamilyRenewalRequests_Amounts", "\"LineCount\" BETWEEN 1 AND 20 AND \"OriginalAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"TotalAmount\" >= 0 AND \"TotalAmount\" = \"OriginalAmount\" - \"DiscountAmount\" AND \"Revision\" > 0"));
        });
        ConfigureTenantEntity<FamilyRenewalLine>(b, "FamilyRenewalLines");
        b.Entity<FamilyRenewalLine>(e => {
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.FinancialSnapshotJson).HasMaxLength(4000); e.Property(x => x.PlanTermsJson).HasMaxLength(2000);
            e.Property(x => x.OriginalAmount).HasPrecision(18, 2); e.Property(x => x.DiscountAmount).HasPrecision(18, 2); e.Property(x => x.FinalAmount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.AcademyId, x.RequestId, x.SportEnrollmentId }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.ResultPaymentId }).IsUnique();
            e.HasOne(x => x.Request).WithMany(x => x.Lines).HasForeignKey(x => new { x.AcademyId, x.RequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SportEnrollment).WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentRequest>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResultPaymentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_FamilyRenewalLines_Amounts", "\"OriginalAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"FinalAmount\" >= 0 AND \"FinalAmount\" = \"OriginalAmount\" - \"DiscountAmount\""));
        });
    }
}
