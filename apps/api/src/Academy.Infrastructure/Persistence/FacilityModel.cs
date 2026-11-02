using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<FacilitySpace> FacilitySpaces => Set<FacilitySpace>();
    public DbSet<FacilityResource> FacilityResources => Set<FacilityResource>();
    public DbSet<ResourceBooking> ResourceBookings => Set<ResourceBooking>();
    public DbSet<ResourceClosure> ResourceClosures => Set<ResourceClosure>();
    public DbSet<GroupResourceSchedule> GroupResourceSchedules => Set<GroupResourceSchedule>();
    public DbSet<TrainingScheduleRevision> TrainingScheduleRevisions => Set<TrainingScheduleRevision>();
    private static void ConfigureFacilities(ModelBuilder b)
    {
        Tenant<Facility>(b); Tenant<FacilitySpace>(b); Tenant<FacilityResource>(b); Tenant<ResourceBooking>(b); Tenant<ResourceClosure>(b);
        Tenant<GroupResourceSchedule>(b); Tenant<TrainingScheduleRevision>(b);
        b.Entity<GroupResourceSchedule>(e => {
            e.HasIndex(x => new { x.AcademyId, x.TrainingGroupId }).IsUnique();
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FacilityResource>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_GroupResourceSchedule_Range", "\"ReservedSeats\" BETWEEN 1 AND 10000 AND \"ValidFrom\" <= \"ValidTo\""));
        });
        b.Entity<TrainingScheduleRevision>(e => {
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Facility>(e => {
            e.Property(x => x.Name).HasMaxLength(120); e.HasIndex(x => new { x.AcademyId, x.BranchId, x.Name }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<FacilitySpace>(e => {
            e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.AcademyId, x.FacilityId, x.Name }).IsUnique();
            e.HasOne<Facility>().WithMany().HasForeignKey(x => new { x.AcademyId, x.FacilityId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<FacilityResource>(e => {
            e.Property(x => x.Name).HasMaxLength(60); e.Property(x => x.ApprovalEvidence).HasMaxLength(500);
            e.HasIndex(x => new { x.AcademyId, x.FacilitySpaceId, x.Name }).IsUnique();
            e.HasOne<FacilitySpace>().WithMany().HasForeignKey(x => new { x.AcademyId, x.FacilitySpaceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_Resource_CapacityHours", "\"Capacity\" BETWEEN 1 AND 10000 AND \"OpensAt\" < \"ClosesAt\" AND cardinality(\"OperatingDays\") BETWEEN 1 AND 7 AND \"OperatingDays\" <@ ARRAY[0,1,2,3,4,5,6]"); t.HasCheckConstraint("CK_Resource_Approval", "(\"ApprovedByUserId\" IS NULL AND \"ApprovedAtUtc\" IS NULL) OR (\"ApprovedByUserId\" IS NOT NULL AND \"ApprovedAtUtc\" IS NOT NULL AND length(\"ApprovalEvidence\") > 0)"); });
        });
        b.Entity<ResourceBooking>(e => {
            e.Property(x => x.ServiceKind).HasMaxLength(30); e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64);
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique(); e.HasIndex(x => new { x.AcademyId, x.ResourceId, x.Date });
            e.HasOne<FacilityResource>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ResourceBooking_SeatsHours", "\"ReservedSeats\" BETWEEN 1 AND \"ApprovedCapacitySnapshot\" AND \"StartTime\" < \"EndTime\""));
        });
        b.Entity<ResourceClosure>(e => {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<FacilityResource>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ResourceClosure_Range", "\"FromDate\" <= \"ToDate\" AND \"StartTime\" < \"EndTime\""));
        });
    }
}
