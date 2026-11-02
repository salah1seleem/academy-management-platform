using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Attendance;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<GroupMediaConsent> GroupMediaConsents => Set<GroupMediaConsent>();
    public DbSet<PrivateMediaConsentBasis> PrivateMediaConsentBases => Set<PrivateMediaConsentBasis>();
    public DbSet<PrivateMediaWithdrawal> PrivateMediaWithdrawals => Set<PrivateMediaWithdrawal>();
    public DbSet<PrivateMediaErasure> PrivateMediaErasures => Set<PrivateMediaErasure>();
    public DbSet<PrivateMediaPurge> PrivateMediaPurges => Set<PrivateMediaPurge>();
    public DbSet<PrivateMediaDownloadBudget> PrivateMediaDownloadBudgets => Set<PrivateMediaDownloadBudget>();
    private static void ConfigureGroupMediaPrivacy(ModelBuilder b)
    {
        Tenant<GroupMediaConsent>(b); Tenant<PrivateMediaConsentBasis>(b);
        b.Entity<GuardianPlayerLink>().HasAlternateKey(x=>new{x.AcademyId,x.Id,x.PlayerId});
        var c=b.Entity<GroupMediaConsent>();
        c.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.PlayerId});
        c.HasOne<GuardianPlayerLink>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.GuardianPlayerLinkId,x.PlayerId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.PlayerId}).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x=>new{x.AcademyId,x.GuardianPlayerLinkId,x.Revision}).IsUnique();
        c.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
        c.Property(x=>x.RequestHash).HasMaxLength(64);
        c.ToTable(t=>t.HasCheckConstraint("CK_GroupMediaConsent_Revision","\"IsActive\" AND \"Revision\">0"));
        var a=b.Entity<PrivateAssetAudience>();
        a.HasAlternateKey(x=>new{x.AcademyId,x.AssetId,x.PlayerId});
        a.HasOne<SportEnrollment>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SportEnrollmentId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        var p=b.Entity<PrivateMediaConsentBasis>();
        p.HasOne<PrivateAssetAudience>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.AssetId,x.PlayerId}).HasPrincipalKey(x=>new{x.AcademyId,x.AssetId,x.PlayerId}).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<GroupMediaConsent>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ConsentId,x.PlayerId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.PlayerId}).OnDelete(DeleteBehavior.Restrict);
        p.HasIndex(x=>new{x.AcademyId,x.AssetId,x.ConsentId}).IsUnique();
        p.ToTable(t=>t.HasCheckConstraint("CK_PrivateMediaConsentBasis_Active","\"IsActive\""));
        b.Entity<PrivateAsset>().HasOne<TrainingSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingSessionId,x.TrainingGroupId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingGroupId}).OnDelete(DeleteBehavior.Restrict);
        Tenant<PrivateMediaWithdrawal>(b);
        var w=b.Entity<PrivateMediaWithdrawal>();
        w.HasOne<PrivateAsset>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.AssetId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        w.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        w.HasIndex(x=>new{x.AcademyId,x.AssetId}).IsUnique();
        w.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
        w.Property(x=>x.Note).HasMaxLength(500);w.Property(x=>x.RequestHash).HasMaxLength(64);
        w.ToTable(t=>t.HasCheckConstraint("CK_PrivateMediaWithdrawal_Active","\"IsActive\""));
        Tenant<PrivateMediaErasure>(b);Tenant<PrivateMediaPurge>(b);Tenant<PrivateMediaDownloadBudget>(b);
        var e=b.Entity<PrivateMediaErasure>();e.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.AssetId});
        e.HasOne<PrivateAsset>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.AssetId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x=>new{x.AcademyId,x.AssetId}).IsUnique();e.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
        e.Property(x=>x.Reason).HasMaxLength(20);e.Property(x=>x.Note).HasMaxLength(500);e.Property(x=>x.RequestHash).HasMaxLength(64);
        e.ToTable(t=>t.HasCheckConstraint("CK_MediaErasure_Source","\"IsActive\" AND ((\"Reason\"='Manual' AND \"ActorUserId\" IS NOT NULL) OR (\"Reason\"='Retention' AND \"ActorUserId\" IS NULL))"));
        var purge=b.Entity<PrivateMediaPurge>();purge.HasOne<PrivateMediaErasure>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ErasureId,x.AssetId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.AssetId}).OnDelete(DeleteBehavior.Restrict);
        purge.HasIndex(x=>new{x.AcademyId,x.AssetId}).IsUnique();purge.ToTable(t=>t.HasCheckConstraint("CK_MediaPurge_Bytes","\"IsActive\" AND \"ReleasedBytes\">=0"));
        var d=b.Entity<PrivateMediaDownloadBudget>();d.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);d.HasIndex(x=>new{x.AcademyId,x.UserId,x.UtcDay}).IsUnique();d.ToTable(t=>t.HasCheckConstraint("CK_MediaDownloadBudget_Bytes","\"IsActive\" AND \"ReservedBytes\">=0"));
        b.Entity<PrivateAsset>().ToTable(t=>t.HasCheckConstraint("CK_MediaRetention_Snapshot","(\"RetainUntilUtc\" IS NULL AND NOT \"DeleteOnExpiry\") OR (\"Purpose\"='Media' AND \"RetainUntilUtc\" IS NOT NULL AND \"RetainUntilUtc\">\"CreatedAtUtc\")"));
    }
}
