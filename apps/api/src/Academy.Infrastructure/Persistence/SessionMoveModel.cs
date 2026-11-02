using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<EnrollmentSessionMove> EnrollmentSessionMoves => Set<EnrollmentSessionMove>();
    public static bool IsSessionAssigned(Guid academy, Guid enrollment, Guid session) => throw new InvalidOperationException("SQL query only");
    private static void ConfigureSessionMoves(ModelBuilder b)
    {
        b.HasDbFunction(typeof(FoundationDbContext).GetMethod(nameof(IsSessionAssigned))!).HasName("academy_session_assigned");
        Tenant<EnrollmentSessionMove>(b);
        b.Entity<TrainingSession>().HasAlternateKey(s => new { s.AcademyId, s.Id, s.TrainingGroupId, s.BranchId, s.SportId });
        b.Entity<EnrollmentSessionMove>(e => {
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.SourceSessionId }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.TargetSessionId }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey });
            e.HasIndex(x => new { x.AcademyId, x.TargetGroupId });
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SourceSessionId, x.SourceGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TargetSessionId, x.TargetGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_SessionMove_DifferentSessions", "\"SourceSessionId\" <> \"TargetSessionId\""));
        });
    }
}
