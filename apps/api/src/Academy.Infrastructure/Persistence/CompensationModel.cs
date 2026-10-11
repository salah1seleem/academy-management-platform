using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<CompensationGrant> CompensationGrants => Set<CompensationGrant>();
    public DbSet<CompensationBooking> CompensationBookings => Set<CompensationBooking>();
    public DbSet<CompensationMovement> CompensationMovements => Set<CompensationMovement>();
    private static void ConfigureCompensation(ModelBuilder b)
    {
        Tenant<CompensationGrant>(b); Tenant<CompensationBooking>(b); Tenant<CompensationMovement>(b);
        b.Entity<CompensationGrant>(e => {
            e.Property(x => x.Cause).HasConversion<string>(); e.Property(x => x.Unit).HasConversion<string>();
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportEnrollmentId, x.BranchId, x.SportId });
            e.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.SourceSessionId }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SourceSessionId, x.SourceGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SourcePeriodId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Compensation_Units", "\"Units\" > 0 AND ((\"Unit\"='Session' AND \"Units\"=1) OR (\"Unit\"='Minute' AND \"Units\"<=1440)) AND \"ValidFrom\"<=\"ValidTo\""));
        });
        b.Entity<CompensationBooking>(e => {
            e.Property(x => x.State).HasConversion<string>(); e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasAlternateKey(x => new { x.AcademyId, x.Id, x.GrantId, x.BranchId });
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId, x.TrainingSessionId }).IsUnique().HasFilter("\"State\"='Reserved'");
            e.HasOne<CompensationGrant>().WithMany().HasForeignKey(x => new { x.AcademyId, x.GrantId, x.SportEnrollmentId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportEnrollmentId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId, x.TrainingGroupId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingGroupId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_CompensationBooking_Units", "\"Units\" > 0 AND \"Units\"<=1440"));
        });
        b.Entity<CompensationMovement>(e => {
            e.HasOne<CompensationBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BookingId, x.GrantId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.GrantId, x.BranchId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PlayerAttendance>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerAttendanceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CompensationMovement>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ReversesMovementId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademyId, x.ReversesMovementId }).IsUnique().HasFilter("\"ReversesMovementId\" IS NOT NULL");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_CompensationMovement_Sign", "(\"ReversesMovementId\" IS NULL AND \"Units\">0) OR (\"ReversesMovementId\" IS NOT NULL AND \"Units\"<0)"));
        });
    }
}
