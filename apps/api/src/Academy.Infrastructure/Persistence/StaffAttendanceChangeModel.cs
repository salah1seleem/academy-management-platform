using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<StaffAttendanceChange> StaffAttendanceChanges => Set<StaffAttendanceChange>();
    private static void ConfigureStaffAttendanceChanges(ModelBuilder b)
    {
        Tenant<StaffAttendanceChange>(b);
        var e = b.Entity<StaffAttendanceChange>();
        e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
        e.HasIndex(x => new { x.AcademyId, x.MembershipId, x.WorkDate, x.CreatedAtUtc });
        e.Property(x => x.RequestKey).HasMaxLength(100);
        e.Property(x => x.RequestHash).HasMaxLength(64);
        e.Property(x => x.BeforeSnapshot).HasColumnType("jsonb");
        e.Property(x => x.AfterSnapshot).HasColumnType("jsonb");
        e.HasOne<StaffPasskeyChallenge>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.VerificationChallengeId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x=>new{x.AcademyId,x.VerificationChallengeId}).IsUnique().HasFilter("\"VerificationChallengeId\" IS NOT NULL");
        e.HasOne<StaffDailyAttendance>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AttendanceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ActorMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_StaffAttendanceChange_Evidence", "\"IsActive\" AND \"BeforeVersion\" >= 0 AND \"AfterVersion\" > 0 AND length(\"RequestKey\") BETWEEN 8 AND 100 AND length(\"RequestHash\")=64 AND jsonb_typeof(\"AfterSnapshot\")='object' AND jsonb_typeof(\"BeforeSnapshot\") IN ('null','object')"));
    }
}
