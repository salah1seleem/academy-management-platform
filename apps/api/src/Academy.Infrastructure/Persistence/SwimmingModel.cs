using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<SwimmingBranchPolicy> SwimmingBranchPolicies => Set<SwimmingBranchPolicy>();
    public DbSet<SportSettingsAudit> SportSettingsAudits => Set<SportSettingsAudit>();
    private static void ConfigureSwimming(ModelBuilder b)
    {
        Tenant<SwimmingBranchPolicy>(b); Tenant<SportSettingsAudit>(b);
        b.Entity<SwimmingBranchPolicy>(e => {
            e.ToTable("SwimmingBranchPolicies");
            e.HasIndex(x => new { x.AcademyId, x.SportId, x.BranchId }).IsUnique();
            e.HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SportSettingsAudit>(e => {
            e.Property(x => x.Before).HasColumnType("jsonb"); e.Property(x => x.After).HasColumnType("jsonb");
            e.HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
