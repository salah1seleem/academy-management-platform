using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<StaffAttendanceException> StaffAttendanceExceptions => Set<StaffAttendanceException>();
    private static void ConfigureStaffAttendanceExceptions(ModelBuilder b)
    {
        Tenant<StaffAttendanceException>(b);
        var e = b.Entity<StaffAttendanceException>();
        e.HasIndex(x => new { x.AcademyId, x.RequestedByUserId, x.RequestKey }).IsUnique();
        e.HasIndex(x => new { x.AcademyId, x.ReviewedByUserId, x.ReviewKey }).IsUnique().HasFilter("\"ReviewKey\" IS NOT NULL");
        e.HasIndex(x => new { x.AcademyId, x.Status, x.BranchId, x.WorkDate });
        e.Property(x => x.Operation).HasMaxLength(10); e.Property(x => x.Status).HasMaxLength(12);
        e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.ReviewNote).HasMaxLength(500);
        e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.ReviewKey).HasMaxLength(100);
        e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.ReviewHash).HasMaxLength(64);
        e.Property(x => x.BeforeSnapshot).HasColumnType("jsonb"); e.Property(x => x.AfterSnapshot).HasColumnType("jsonb");
        e.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EmployeeId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ReviewedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<StaffDailyAttendance>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AttendanceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_StaffException_Decision", """
          "IsActive" AND "Operation" IN ('CheckIn','CheckOut') AND "Status" IN ('Pending','Approved','Rejected')
          AND length(trim("Reason")) BETWEEN 5 AND 500 AND "ClaimedAtUtc" <= "CreatedAtUtc"
          AND (("Status"='Pending' AND "ReviewedByUserId" IS NULL AND "ReviewedByMembershipId" IS NULL
            AND "ReviewedAtUtc" IS NULL AND "ReviewKey" IS NULL AND "ReviewHash" IS NULL AND "ReviewNote" IS NULL
            AND "AttendanceId" IS NULL AND "BeforeSnapshot" IS NULL AND "AfterSnapshot" IS NULL)
          OR ("Status" <> 'Pending' AND "ReviewedByUserId" IS NOT NULL AND "ReviewedByUserId" <> "RequestedByUserId"
            AND "ReviewedByMembershipId" IS NOT NULL AND "ReviewedAtUtc" IS NOT NULL AND "ReviewedAtUtc" >= "CreatedAtUtc"
            AND "ReviewKey" IS NOT NULL AND "ReviewHash" IS NOT NULL
            AND (("Status"='Approved' AND "AttendanceId" IS NOT NULL AND "BeforeSnapshot" IS NOT NULL AND "AfterSnapshot" IS NOT NULL)
              OR ("Status"='Rejected' AND "ReviewNote" IS NOT NULL AND length(trim("ReviewNote")) BETWEEN 5 AND 500
                AND "AttendanceId" IS NULL AND "BeforeSnapshot" IS NULL AND "AfterSnapshot" IS NULL))))
          """));
    }
}
