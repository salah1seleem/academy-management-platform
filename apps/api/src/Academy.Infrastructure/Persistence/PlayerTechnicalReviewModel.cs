using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<PlayerTechnicalReviewRequest> PlayerTechnicalReviewRequests => Set<PlayerTechnicalReviewRequest>();
    public DbSet<PlayerTechnicalReviewDecision> PlayerTechnicalReviewDecisions => Set<PlayerTechnicalReviewDecision>();
    private static void ConfigurePlayerTechnicalReviews(ModelBuilder b)
    {
        Tenant<PlayerTechnicalReviewRequest>(b);Tenant<PlayerTechnicalReviewDecision>(b);
        var r=b.Entity<PlayerTechnicalReviewRequest>();
        r.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.PlanId});r.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId});
        r.HasIndex(x=>new{x.AcademyId,x.PlanId,x.Sequence}).IsUnique();r.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
        r.HasOne<PlayerSkillPlan>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.PlanId,x.CurriculumId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.CurriculumId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<SkillCurriculum>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CurriculumId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<SportEnrollment>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<PlayerTechnicalReviewRequest>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.PreviousRequestId,x.PlanId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.PlanId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        r.Property(x=>x.LevelName).HasMaxLength(100);r.Property(x=>x.CurriculumTitle).HasMaxLength(100);r.Property(x=>x.ActorName).HasMaxLength(160);r.Property(x=>x.RequestHash).HasMaxLength(64);
        r.ToTable(t=>t.HasCheckConstraint("CK_PlayerTechnicalReviewRequest_Source","\"IsActive\" AND \"Sequence\">0 AND \"SourceReviewSequence\">0 AND cardinality(\"EvidenceReviewIds\") BETWEEN 1 AND 20"));
        var d=b.Entity<PlayerTechnicalReviewDecision>();
        d.HasIndex(x=>new{x.AcademyId,x.TechnicalRequestId}).IsUnique();d.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
        d.HasOne<PlayerTechnicalReviewRequest>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TechnicalRequestId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<TrainingLevel>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RecommendedLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        d.Property(x=>x.Outcome).HasMaxLength(24);d.Property(x=>x.RecommendedLevelName).HasMaxLength(100);d.Property(x=>x.FamilyNote).HasMaxLength(500);d.Property(x=>x.ActorName).HasMaxLength(160);d.Property(x=>x.RequestHash).HasMaxLength(64);
        d.ToTable(t=>t.HasCheckConstraint("CK_PlayerTechnicalReviewDecision_Outcome","\"IsActive\" AND \"Outcome\" IN ('ReadyForPlacement','NeedsPractice','ContextChanged') AND ((\"Outcome\"='ReadyForPlacement' AND \"RecommendedLevelId\" IS NOT NULL AND \"RecommendedLevelName\" IS NOT NULL) OR (\"Outcome\"<>'ReadyForPlacement' AND \"RecommendedLevelId\" IS NULL AND \"RecommendedLevelName\" IS NULL))"));
    }
}
