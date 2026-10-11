using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<EnrollmentGroupTransfer> EnrollmentGroupTransfers => Set<EnrollmentGroupTransfer>();

    // SQL-only, parameterized by the row's trusted tenant. Not an HTTP entry point
    // and never relies on pooled PostgreSQL session variables or wall-clock SQL.
    public static Guid? AssignedGroupAt(Guid academy, Guid enrollment, DateOnly date) => throw new InvalidOperationException("SQL query only");

    private static void ConfigureGroupTransfers(ModelBuilder b)
    {
        b.HasDbFunction(typeof(FoundationDbContext).GetMethod(nameof(AssignedGroupAt))!).HasName("academy_assigned_group_at");
        Tenant<EnrollmentGroupTransfer>(b);
        b.Entity<SportEnrollment>().HasAlternateKey(e => new { e.AcademyId, e.Id, e.BranchId, e.SportId });
        b.Entity<EnrollmentGroupTransfer>(e => {
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.EffectiveDate }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.TargetGroupId, x.EffectiveDate });
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SourceGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TargetGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_GroupTransfer_DifferentGroups", "\"SourceGroupId\" <> \"TargetGroupId\""));
        });
    }
}
