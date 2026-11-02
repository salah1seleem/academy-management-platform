using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    private static void ConfigureAttendanceCorrections(ModelBuilder b)
    {
        Tenant<AttendanceCorrection>(b);var e=b.Entity<AttendanceCorrection>();
        e.Property(x=>x.Kind).HasConversion<string>().HasMaxLength(20);e.Property(x=>x.State).HasConversion<string>().HasMaxLength(20);
        e.Property(x=>x.Payload).HasColumnType("jsonb");e.Property(x=>x.Reason).HasMaxLength(500);e.Property(x=>x.DecisionReason).HasMaxLength(500);e.Property(x=>x.RequestKey).HasMaxLength(100);e.Property(x=>x.RequestHash).HasMaxLength(64);
        e.HasIndex(x=>new{x.AcademyId,x.RequestedByUserId,x.RequestKey}).IsUnique();e.HasIndex(x=>new{x.AcademyId,x.State,x.CreatedAtUtc});
        e.HasOne<TrainingSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingSessionId,x.TrainingGroupId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingGroupId}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RequestedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.DecidedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
