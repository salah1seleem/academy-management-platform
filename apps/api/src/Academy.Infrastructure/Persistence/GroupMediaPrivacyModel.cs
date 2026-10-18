using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<GroupMediaConsent> GroupMediaConsents => Set<GroupMediaConsent>();
    public DbSet<PrivateMediaConsentBasis> PrivateMediaConsentBases => Set<PrivateMediaConsentBasis>();
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
    }
}
