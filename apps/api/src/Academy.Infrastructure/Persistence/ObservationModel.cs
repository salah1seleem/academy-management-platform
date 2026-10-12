using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<ResourceObservation> ResourceObservations => Set<ResourceObservation>();
    private static void ConfigureObservations(ModelBuilder b)
    {
        Tenant<ResourceObservation>(b);
        b.Entity<ResourceObservation>(e => {
            e.Property(x => x.ExpectedBasis).HasMaxLength(20); e.Property(x => x.Source).HasMaxLength(20); e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestHash).HasMaxLength(64);
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique(); e.HasIndex(x => new { x.AcademyId, x.ResourceBookingId, x.Sequence }).IsUnique();
            e.HasOne<ResourceBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceBookingId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FacilityResource>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingSession>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingSessionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Observation_Counts", "\"Count\" BETWEEN 0 AND 10000 AND \"Sequence\">0 AND \"ExpectedSnapshot\">=0 AND (\"RecordedPresentSnapshot\" IS NULL OR \"RecordedPresentSnapshot\">=0) AND (\"RecordedTotalSnapshot\" IS NULL OR \"RecordedTotalSnapshot\">=\"RecordedPresentSnapshot\") AND \"ExpectedBasis\" IN ('Roster','ReservedSeats') AND \"Source\" IN ('Manual','DemoSeed')"));
        });
    }
}
