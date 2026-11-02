using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<PlayerAchievementCertificate> PlayerAchievementCertificates=>Set<PlayerAchievementCertificate>();
    public DbSet<PlayerCertificateWithdrawal> PlayerCertificateWithdrawals=>Set<PlayerCertificateWithdrawal>();
    private static void ConfigurePlayerCertificates(ModelBuilder b)
    {
        Tenant<PlayerAchievementCertificate>(b);Tenant<PlayerCertificateWithdrawal>(b);
        b.Entity<PlayerLevelReview>().HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId});
        var c=b.Entity<PlayerAchievementCertificate>();c.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId});
        c.HasOne<PlayerLevelReview>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SourceLevelReviewId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x=>new{x.AcademyId,x.SourceLevelReviewId}).IsUnique();c.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
        c.Property(x=>x.AcademyName).HasMaxLength(200);c.Property(x=>x.PlayerName).HasMaxLength(200);c.Property(x=>x.SportName).HasMaxLength(100);c.Property(x=>x.LevelName).HasMaxLength(100);c.Property(x=>x.ActorName).HasMaxLength(160);c.Property(x=>x.RequestHash).HasMaxLength(64);
        c.ToTable(t=>t.HasCheckConstraint("CK_PlayerCertificate_Source","\"IsActive\" AND length(trim(\"PlayerName\"))>0 AND length(trim(\"LevelName\"))>0 AND \"MilestoneAtUtc\"<=\"CreatedAtUtc\""));
        var w=b.Entity<PlayerCertificateWithdrawal>();w.HasOne<PlayerAchievementCertificate>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CertificateId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        w.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);w.HasIndex(x=>new{x.AcademyId,x.CertificateId}).IsUnique();w.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();w.Property(x=>x.ActorName).HasMaxLength(160);w.Property(x=>x.FamilyNote).HasMaxLength(500);w.Property(x=>x.RequestHash).HasMaxLength(64);
        w.ToTable(t=>t.HasCheckConstraint("CK_PlayerCertificateWithdrawal_Active","\"IsActive\""));
    }
}
