using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<AcademyPlatformProfile> AcademyPlatformProfiles => Set<AcademyPlatformProfile>();
    private static void ConfigurePlatformProfiles(ModelBuilder b)
    {
        b.Entity<AcademyPlatformProfile>(e => {
            e.HasKey(x => x.AcademyId);
            e.HasOne<Tenancy.Academy>().WithOne().HasForeignKey<AcademyPlatformProfile>(x => x.AcademyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.OperationalState).HasMaxLength(16);
            e.Property(x => x.Location).HasMaxLength(240); e.Property(x => x.ContactPhone).HasMaxLength(20);
            e.Property(x => x.CommercialState).HasMaxLength(20); e.Property(x => x.PlatformFee).HasPrecision(18,2);
            e.Property(x => x.FeeCurrency).HasMaxLength(3); e.Property(x => x.BillingPeriod).HasMaxLength(10);
            e.Property(x => x.BundledIconKey).HasMaxLength(80);
            e.Property(x => x.EditVersion).IsConcurrencyToken();
            e.ToTable(t => {
                t.HasCheckConstraint("CK_PlatformProfile_State", "\"OperationalState\" IN ('Draft','Active','Suspended') AND \"CommercialState\" IN ('Unspecified','Trial','Subscribed','Expired')");
                t.HasCheckConstraint("CK_PlatformProfile_Fee", "(\"PlatformFee\" IS NULL OR \"PlatformFee\" >= 0) AND \"FeeCurrency\" IN ('EGP','USD','SAR','AED') AND \"BillingPeriod\" IN ('Monthly','Yearly') AND (\"SubscriptionTo\" IS NULL OR \"SubscriptionFrom\" IS NOT NULL AND \"SubscriptionTo\" >= \"SubscriptionFrom\")");
            });
        });
    }
}
