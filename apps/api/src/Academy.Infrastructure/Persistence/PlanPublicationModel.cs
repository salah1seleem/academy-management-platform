using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<PlanBranchPublication> PlanBranchPublications => Set<PlanBranchPublication>();
    public DbSet<PlanPriceVersion> PlanPriceVersions => Set<PlanPriceVersion>();
    private static void ConfigurePlanPublication(ModelBuilder b)
    {
        Tenant<PlanBranchPublication>(b); Tenant<PlanPriceVersion>(b);
        b.Entity<SubscriptionPlan>().Property(x => x.AllBranches).HasDefaultValue(true);
        b.Entity<PlanBranchPublication>(e => {
            e.HasIndex(x => new { x.AcademyId, x.SubscriptionPlanId, x.BranchId }).IsUnique();
            e.HasOne(x => x.SubscriptionPlan).WithMany(x => x.BranchPublications).HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlanPriceVersion>(e => {
            e.HasIndex(x => new { x.AcademyId, x.SubscriptionPlanId, x.Revision }).IsUnique();
            e.Property(x => x.ArabicName).HasMaxLength(160); e.Property(x => x.PlanType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Price).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        Terms<RenewalRequest>(b); Terms<SubscriptionPeriod>(b); Terms<NewEnrollmentRequest>(b);
    }
    private static void Terms<T>(ModelBuilder b) where T : class, IPlanTermsSnapshot
    {
        b.Entity<T>().Property(x => x.PlanNameSnapshot).HasMaxLength(160);
        b.Entity<T>().Property(x => x.PlanTypeSnapshot).HasConversion<string>().HasMaxLength(20);
    }
}
