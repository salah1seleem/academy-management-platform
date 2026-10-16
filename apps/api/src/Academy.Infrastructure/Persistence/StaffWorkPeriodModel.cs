using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<StaffWorkPeriod> StaffWorkPeriods => Set<StaffWorkPeriod>();
    private static void ConfigureStaffWorkPeriods(ModelBuilder b)
    {
        Tenant<StaffWorkPeriod>(b); var e=b.Entity<StaffWorkPeriod>();
        e.Property(x=>x.TrustSource).HasMaxLength(24); e.Property(x=>x.TimeZoneId).HasMaxLength(80);
        e.HasIndex(x=>new{x.AcademyId,x.MembershipId}).IsUnique().HasFilter("\"EndTime\" IS NULL");
        e.HasIndex(x=>new{x.AcademyId,x.AttendanceId,x.StartTime});
        e.HasOne<StaffDailyAttendance>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.AttendanceId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.MembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t=>t.HasCheckConstraint("CK_StaffWorkPeriod_Bounds", """
          "IsActive" AND (("EndDate" IS NULL AND "EndTime" IS NULL) OR ("EndDate" IS NOT NULL AND "EndTime" IS NOT NULL AND ("EndDate"+"EndTime") >= ("StartDate"+"StartTime")))
          AND (("TrustSource"='LegacyLocal' AND "StartUtc" IS NULL AND "EndUtc" IS NULL AND "TimeZoneId" IS NULL)
            OR ("TrustSource" IN ('AdminDeclared','ServerVerified','MixedVerifiedEnd') AND "StartUtc" IS NOT NULL AND "TimeZoneId" IS NOT NULL AND (("EndTime" IS NULL AND "EndUtc" IS NULL AND "TrustSource"<>'MixedVerifiedEnd') OR ("EndTime" IS NOT NULL AND "EndUtc" IS NOT NULL AND "EndUtc" >= "StartUtc"))))
          """));
    }
}
