using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<AcademyConfigurationVersion> AcademyConfigurationVersions => Set<AcademyConfigurationVersion>();
    public DbSet<AcademyFeatureEntitlement> AcademyFeatureEntitlements => Set<AcademyFeatureEntitlement>();
    public DbSet<ConfigurationTransition> ConfigurationTransitions => Set<ConfigurationTransition>();

    private static void ConfigureVersionedSettings(ModelBuilder b)
    {
        ConfigureTenantEntity<AcademyConfigurationVersion>(b, "AcademyConfigurationVersions");
        ConfigureTenantEntity<AcademyFeatureEntitlement>(b, "AcademyFeatureEntitlements");
        ConfigureTenantEntity<ConfigurationTransition>(b, "ConfigurationTransitions");
        b.Entity<AcademyConfigurationVersion>(e => {
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.ContentHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => new { x.AcademyId, x.Revision }).IsUnique();
            e.HasIndex(x => x.AcademyId).IsUnique().HasFilter("\"State\" = 'Active'");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AcademyFeatureEntitlement>(e => {
            e.Property(x => x.Feature).HasMaxLength(50); e.Property(x => x.PlanCode).HasMaxLength(40);
            e.HasIndex(x => new { x.AcademyId, x.Feature }).IsUnique();
        });
        b.Entity<ConfigurationTransition>(e => {
            e.Property(x => x.From).HasConversion<string>().HasMaxLength(24); e.Property(x => x.To).HasConversion<string>().HasMaxLength(24);
            e.HasOne<AcademyConfigurationVersion>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ConfigurationId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
