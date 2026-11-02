using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Content;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<PlatformAdministrator> PlatformAdministrators => Set<PlatformAdministrator>();
    public DbSet<PlatformAudit> PlatformAudits => Set<PlatformAudit>();
    public DbSet<PlatformMfaUse> PlatformMfaUses => Set<PlatformMfaUse>();
    public DbSet<HelpVideoRevision> HelpVideoRevisions => Set<HelpVideoRevision>();
    private static void ConfigurePlatformIdentity(ModelBuilder b)
    {
        b.Entity<HelpVideoRevision>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.LessonId).HasMaxLength(80);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Description).HasMaxLength(1600);
            e.Property(x => x.AlternativeText).HasMaxLength(300);
            e.Property(x => x.VideoFile).HasMaxLength(80);
            e.Property(x => x.ThumbnailFile).HasMaxLength(80);
            e.Property(x => x.EditVersion).IsConcurrencyToken();
            e.HasIndex(x => new { x.LessonId, x.Revision }).IsUnique();
            e.HasIndex(x => x.LessonId).IsUnique().HasFilter("\"State\" = 'Published'");
            e.ToTable(t => t.HasCheckConstraint("CK_HelpVideo_Lifecycle", """ "Revision" > 0 AND "EditVersion" > 0 AND "State" IN ('Draft','Published','Archived') AND ("State" <> 'Published' OR ("VideoFile" IS NOT NULL AND "ThumbnailFile" IS NOT NULL AND "DurationSeconds" IS NOT NULL AND "DurationSeconds" > 0 AND "PublishedAtUtc" IS NOT NULL)) """));
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });
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
