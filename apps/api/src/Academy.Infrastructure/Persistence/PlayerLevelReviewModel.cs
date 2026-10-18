using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<PlayerLevelReview> PlayerLevelReviews=>Set<PlayerLevelReview>();
    private static void ConfigurePlayerLevelReviews(ModelBuilder b)
    {
        Tenant<PlayerLevelReview>(b);var r=b.Entity<PlayerLevelReview>();
        b.Entity<PlayerTechnicalReviewDecision>().HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId});
        b.Entity<EnrollmentGroupTransfer>().HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId});
        r.HasOne<PlayerTechnicalReviewDecision>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TechnicalDecisionId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<EnrollmentGroupTransfer>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.GroupTransferId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasIndex(x=>new{x.AcademyId,x.TechnicalDecisionId}).IsUnique().HasFilter("\"TechnicalDecisionId\" IS NOT NULL");
        r.ToTable(t=>t.HasCheckConstraint("CK_LevelReview_Promotion","(\"TechnicalDecisionId\" IS NULL AND \"GroupTransferId\" IS NULL) OR (\"TechnicalDecisionId\" IS NOT NULL AND \"GroupTransferId\" IS NOT NULL AND \"Action\"='Changed')"));
        r.HasOne<SportEnrollment>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<TrainingLevel>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.FromLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<TrainingLevel>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ToLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        r.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();r.HasIndex(x=>new{x.AcademyId,x.SportEnrollmentId,x.Revision}).IsUnique();
        r.ToTable(t=>t.HasCheckConstraint("CK_PlayerLevelReview_Revision","\"Revision\">0"));
        r.Property(x=>x.FromLevelName).HasMaxLength(100);r.Property(x=>x.ToLevelName).HasMaxLength(100);r.Property(x=>x.ActorName).HasMaxLength(160);r.Property(x=>x.Action).HasMaxLength(12);r.Property(x=>x.PrivateNote).HasMaxLength(500);r.Property(x=>x.RequestHash).HasMaxLength(64);
        r.ToTable(t=>t.HasCheckConstraint("CK_PlayerLevelReview_Action","\"IsActive\" AND \"Action\" IN ('Reviewed','Changed','Cleared') AND (\"ToLevelId\" IS NULL AND \"ToLevelName\" IS NULL OR \"ToLevelId\" IS NOT NULL AND length(trim(\"ToLevelName\"))>0) AND (\"FromLevelId\" IS NULL AND \"FromLevelName\" IS NULL OR \"FromLevelId\" IS NOT NULL AND length(trim(\"FromLevelName\"))>0)"));
    }
}
