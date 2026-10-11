using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<TrainingLevel> TrainingLevels => Set<TrainingLevel>();
    public DbSet<GroupPlacementRevision> GroupPlacementRevisions => Set<GroupPlacementRevision>();
    private static void ConfigurePlacement(ModelBuilder b)
    {
        Tenant<TrainingLevel>(b); Tenant<GroupPlacementRevision>(b);
        b.Entity<TrainingLevel>(e => {
            e.Property(x => x.ArabicName).HasMaxLength(100);
            e.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
            e.HasIndex(x => new { x.AcademyId, x.SportId, x.ArabicName }).IsUnique();
            e.HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_TrainingLevel_Rank", "\"Rank\" BETWEEN 0 AND 1000"));
        });
        b.Entity<TrainingGroup>().HasOne<TrainingLevel>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingLevelId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SportEnrollment>().HasOne<TrainingLevel>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingLevelId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SportEnrollment>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LevelReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<GroupPlacementRevision>(e => {
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
