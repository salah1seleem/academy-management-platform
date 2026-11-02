using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<PlayerDevelopmentGoal> PlayerDevelopmentGoals => Set<PlayerDevelopmentGoal>();
    public DbSet<PlayerGoalChange> PlayerGoalChanges => Set<PlayerGoalChange>();
    private static void ConfigurePlayerGoals(ModelBuilder b)
    {
        Tenant<PlayerDevelopmentGoal>(b); Tenant<PlayerGoalChange>(b);
        var g = b.Entity<PlayerDevelopmentGoal>();
        g.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
        g.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        g.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        g.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.State });
        g.Property(x => x.Title).HasMaxLength(100); g.Property(x => x.Target).HasMaxLength(500); g.Property(x => x.State).HasMaxLength(16);
        g.ToTable(t => t.HasCheckConstraint("CK_PlayerGoal_State", "\"IsActive\" AND \"Revision\" > 0 AND length(trim(\"Title\")) > 0 AND length(trim(\"Target\")) > 0 AND \"State\" IN ('Draft','Published','Achieved','Withdrawn') AND (\"State\" NOT IN ('Published','Achieved') OR \"PublishedAtUtc\" IS NOT NULL)"));
        var c = b.Entity<PlayerGoalChange>();
        c.HasOne<PlayerDevelopmentGoal>().WithMany().HasForeignKey(x => new { x.AcademyId, x.GoalId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestId }).IsUnique();
        c.HasIndex(x => new { x.AcademyId, x.GoalId, x.Revision }).IsUnique();
        c.Property(x => x.RequestHash).HasMaxLength(64); c.Property(x => x.ActorName).HasMaxLength(160); c.Property(x => x.Action).HasMaxLength(16);
        c.Property(x => x.Note).HasMaxLength(500); c.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        c.ToTable(t => t.HasCheckConstraint("CK_PlayerGoalChange_Action", "\"IsActive\" AND \"Revision\" > 0 AND \"Action\" IN ('Created','Edited','Published','Reviewed','Achieved','Withdrawn')"));
    }
    private void GuardPlayerGoalHistory()
    {
        if (ChangeTracker.Entries<PlayerGoalChange>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("مراجعات الأهداف محفوظة ولا تعدل أو تحذف.");
        foreach (var e in ChangeTracker.Entries<PlayerDevelopmentGoal>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            if (e.State == EntityState.Deleted || new[] { "Id", "AcademyId", "SportEnrollmentId", "SportId", "CreatedByUserId", "CreatedAtUtc", "IsActive" }.Any(p => e.Property(p).IsModified)
                || e.Entity.Revision != e.OriginalValues.GetValue<int>("Revision") + 1
                || e.OriginalValues.GetValue<string>("State") != "Draft" && new[] { "Title", "Target", "ReviewOn", "PublishedAtUtc" }.Any(p => e.Property(p).IsModified))
                throw new InvalidOperationException("سياق الهدف والمنشور ثابتان؛ سجل المراجعة أو أنشئ هدفًا جديدًا.");
        }
    }
}
