using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<CommunityCampaign> CommunityCampaigns => Set<CommunityCampaign>();
    public DbSet<CommunityCampaignRevision> CommunityCampaignRevisions => Set<CommunityCampaignRevision>();
    public DbSet<CommunityCampaignTarget> CommunityCampaignTargets => Set<CommunityCampaignTarget>();
    public DbSet<CommunityCampaignReview> CommunityCampaignReviews => Set<CommunityCampaignReview>();
    public DbSet<GuardianSponsorPreference> GuardianSponsorPreferences => Set<GuardianSponsorPreference>();
    public DbSet<CommunityAdEvent> CommunityAdEvents => Set<CommunityAdEvent>();
    private static void ConfigureCommunityCampaigns(ModelBuilder b)
    {
        Tenant<CommunityCampaign>(b);Tenant<CommunityCampaignRevision>(b);Tenant<CommunityCampaignTarget>(b);Tenant<CommunityCampaignReview>(b);Tenant<GuardianSponsorPreference>(b);Tenant<CommunityAdEvent>(b);
        var c=b.Entity<CommunityCampaign>();c.Property(x=>x.InternalName).HasMaxLength(120);c.Property(x=>x.Advertiser).HasMaxLength(120);
        var r=b.Entity<CommunityCampaignRevision>();
        r.Property(x=>x.Advertiser).HasMaxLength(120);
        r.HasOne<CommunityCampaign>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CampaignId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<PrivateAsset>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ImageAssetId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CreatedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasIndex(x=>new{x.AcademyId,x.CampaignId,x.Revision}).IsUnique();r.HasIndex(x=>new{x.AcademyId,x.CreatedByMembershipId,x.RequestId}).IsUnique();
        r.Property(x=>x.Title).HasMaxLength(100);r.Property(x=>x.Body).HasMaxLength(1000);r.Property(x=>x.ButtonText).HasMaxLength(40);r.Property(x=>x.ImageAlt).HasMaxLength(200);r.Property(x=>x.State).HasMaxLength(16);r.Property(x=>x.RequestHash).HasMaxLength(64);
        r.ToTable(t=>t.HasCheckConstraint("CK_CommunityCampaignRevision_Window","\"IsActive\" AND \"Revision\">0 AND \"EndsAtUtc\">\"StartsAtUtc\" AND \"Priority\" BETWEEN 0 AND 10 AND \"State\" IN ('Draft','PendingApproval','Approved','Paused','Rejected')"));
        var t=b.Entity<CommunityCampaignTarget>();
        t.HasOne<CommunityCampaignRevision>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RevisionId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        t.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        t.HasAlternateKey(x=>new{x.AcademyId,x.RevisionId,x.BranchId});t.Property(x=>x.Decision).HasMaxLength(16);t.ToTable(x=>x.HasCheckConstraint("CK_CommunityCampaignTarget_Decision","\"Decision\" IN ('Pending','Approved','Rejected','Revoked')"));
        var v=b.Entity<CommunityCampaignReview>();v.HasOne<CommunityCampaignRevision>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RevisionId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        v.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        v.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        v.HasOne<CommunityCampaignTarget>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RevisionId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.RevisionId,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
        v.HasIndex(x=>new{x.AcademyId,x.ActorMembershipId,x.RequestId}).IsUnique();v.Property(x=>x.Decision).HasMaxLength(16);v.Property(x=>x.Note).HasMaxLength(500);v.Property(x=>x.RequestHash).HasMaxLength(64);
        var p=b.Entity<GuardianSponsorPreference>();p.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);p.HasIndex(x=>new{x.AcademyId,x.UserId}).IsUnique();
        var e=b.Entity<CommunityAdEvent>();e.HasOne<CommunityCampaignRevision>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RevisionId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<CommunityCampaignTarget>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RevisionId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.RevisionId,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x=>new{x.AcademyId,x.RevisionId,x.UserId,x.ViewDate,x.Kind}).IsUnique();e.Property(x=>x.Kind).HasMaxLength(16);e.ToTable(x=>x.HasCheckConstraint("CK_CommunityAdEvent_Kind","\"Kind\" IN ('Impression','Click','Hide') AND \"IsActive\""));
    }
}
