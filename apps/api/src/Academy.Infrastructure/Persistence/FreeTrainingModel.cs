using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<FreeTrainingApproval> FreeTrainingApprovals => Set<FreeTrainingApproval>();
    private static void ConfigureFreeTraining(ModelBuilder b)
    {
        ConfigureTenantEntity<FreeTrainingApproval>(b, "FreeTrainingApprovals");
        b.Entity<FreeTrainingApproval>(e => {
            e.Property(x => x.WaivedAmount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.IdempotencyKey).HasMaxLength(80);
            e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.QuoteHash).HasMaxLength(64);
            e.Property(x => x.PlanTermsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.CreatedAtUtc });
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ActorMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_FreeTrainingApproval_Source", "\"WaivedAmount\" >= 0 AND length(btrim(\"Reason\")) BETWEEN 1 AND 500 AND length(\"IdempotencyKey\") BETWEEN 8 AND 80 AND length(\"RequestHash\") = 64 AND length(\"QuoteHash\") = 64 AND (\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\") AND (\"InitialSessions\" IS NULL OR \"InitialSessions\" > 0)"));
        });
        b.Entity<SubscriptionPeriod>().HasOne(x => x.FreeTrainingApproval).WithMany().HasForeignKey(x => new { x.AcademyId, x.FreeTrainingApprovalId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SubscriptionPeriod>().HasIndex(x => new { x.AcademyId, x.FreeTrainingApprovalId }).IsUnique();
    }
}
