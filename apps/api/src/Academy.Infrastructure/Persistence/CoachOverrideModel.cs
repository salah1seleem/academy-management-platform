using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<SessionCoachOverride> SessionCoachOverrides => Set<SessionCoachOverride>();
    public static bool IsSessionStaffAssigned(Guid academy, Guid membership, Guid session) => throw new InvalidOperationException("SQL query only");
    private static void ConfigureCoachOverrides(ModelBuilder b)
    {
        Tenant<SessionCoachOverride>(b);
        b.HasDbFunction(typeof(FoundationDbContext).GetMethod(nameof(IsSessionStaffAssigned))!).HasName("academy_session_staff_assigned");
        b.Entity<Academy.Infrastructure.Evaluations.PlayerEvaluation>().HasOne<TrainingSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingSessionId,x.TrainingGroupId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingGroupId}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SessionCoachOverride>(e => {
            e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.QualificationEvidence).HasMaxLength(500);
            e.Property(x => x.PayBasis).HasMaxLength(40); e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AcademyId, x.TrainingSessionId, x.OriginalCoachMembershipId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
            e.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.OriginalCoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ActualCoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SessionCoachOverride>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PreviousDecisionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_CoachOverride_Review", "\"Sequence\">0 AND length(btrim(\"Reason\"))>0 AND length(btrim(\"QualificationEvidence\"))>0 AND \"StartTime\"<\"EndTime\" AND \"PayBasis\"='ContractReviewPending'"));
        });
    }
}
