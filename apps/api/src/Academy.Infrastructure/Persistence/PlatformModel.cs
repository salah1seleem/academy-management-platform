using Academy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<PlatformAdministrator> PlatformAdministrators => Set<PlatformAdministrator>();
    public DbSet<PlatformAudit> PlatformAudits => Set<PlatformAudit>();
    public DbSet<PlatformMfaUse> PlatformMfaUses => Set<PlatformMfaUse>();
    private static void ConfigurePlatformIdentity(ModelBuilder b)
    {
        b.Entity<PlatformMfaUse>(e => {
            e.HasKey(x => x.Id); e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.UserId, x.Fingerprint, x.AtUtc });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlatformAdministrator>(e => {
            e.ToTable("PlatformAdministrators"); e.HasKey(x => x.UserId);
            e.Property(x => x.LastMfaFingerprint).HasMaxLength(64);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlatformAudit>(e => {
            e.ToTable("PlatformAudits"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(80); e.Property(x => x.Details).HasColumnType("jsonb");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenancy.Academy>().WithMany().HasForeignKey(x => x.TargetAcademyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TargetAcademyId, x.AtUtc });
        });
    }
}
