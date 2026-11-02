using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<DiscountRuleVersion> DiscountRuleVersions => Set<DiscountRuleVersion>();
    private static void ConfigureDiscountPricing(ModelBuilder b)
    {
        b.Entity<DiscountRule>(e => {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).HasDefaultValue(RenewalDiscountType.Percentage);
            e.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("EGP");
            e.Property(x => x.FixedAmount).HasPrecision(18, 2); e.Property(x => x.MaximumAmount).HasPrecision(18, 2);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        Tenant<DiscountRuleVersion>(b);
        b.Entity<DiscountRuleVersion>(e => {
            e.HasAlternateKey(x => new { x.AcademyId, x.DiscountRuleId, x.Revision }); e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasOne<DiscountRule>().WithMany().HasForeignKey(x => new { x.AcademyId, x.DiscountRuleId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        foreach (var type in new[] { typeof(RenewalRequest), typeof(NewEnrollmentRequest), typeof(Receipt) })
            b.Entity(type).HasOne(typeof(DiscountRuleVersion)).WithMany().HasForeignKey("AcademyId", "DiscountRuleIdSnapshot", "DiscountRuleRevisionSnapshot").HasPrincipalKey("AcademyId", "DiscountRuleId", "Revision").OnDelete(DeleteBehavior.Restrict);
        b.Entity<NewEnrollmentRequest>().Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(20);
        b.Entity<NewEnrollmentRequest>().Property(x => x.DiscountValue).HasPrecision(18, 2);
        b.Entity<NewEnrollmentRequest>().Property(x => x.DiscountAmount).HasPrecision(18, 2);
        b.Entity<NewEnrollmentRequest>().Property(x => x.DiscountReason).HasMaxLength(500);
        b.Entity<Receipt>().Property(x => x.DiscountReason).HasMaxLength(500);
    }
}
