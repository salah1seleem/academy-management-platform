using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<AccessEntitlement> AccessEntitlements => Set<AccessEntitlement>();
    public DbSet<EntryVisit> EntryVisits => Set<EntryVisit>();
    private static void ConfigureVisits(ModelBuilder b)
    {
        Tenant<AccessEntitlement>(b); Tenant<EntryVisit>(b);
        b.Entity<AccessEntitlement>(e => {
            e.Property(x => x.Basis).HasMaxLength(60);
            e.HasOne<ResourceBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceBookingId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FacilityResource>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GuardianProfile>().WithMany().HasForeignKey(x => new { x.AcademyId, x.GuardianId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPeriodId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CompensationBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CompensationBookingId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EntryVisit>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AccompanyingVisitId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorizedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Access_Kind", "\"ValidFromUtc\"<\"ValidToUtc\" AND ((\"PersonKind\"=1 AND \"PlayerId\" IS NOT NULL AND \"SportEnrollmentId\" IS NOT NULL AND \"GuardianId\" IS NULL AND \"AccompanyingVisitId\" IS NULL AND ((\"SubscriptionPeriodId\" IS NOT NULL)::int+(\"CompensationBookingId\" IS NOT NULL)::int)=1) OR (\"PersonKind\"=2 AND \"PlayerId\" IS NULL AND \"SportEnrollmentId\" IS NULL AND \"GuardianId\" IS NOT NULL AND \"AccompanyingVisitId\" IS NOT NULL AND \"SubscriptionPeriodId\" IS NULL AND \"CompensationBookingId\" IS NULL))"));
        });
        b.Entity<EntryVisit>(e => {
            e.Property(x => x.EntryKey).HasMaxLength(100); e.Property(x => x.EntryHash).HasMaxLength(64); e.Property(x => x.ExitKey).HasMaxLength(100); e.Property(x => x.ExitHash).HasMaxLength(64);
            e.HasOne<AccessEntitlement>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AccessEntitlementId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ResourceBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceBookingId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.EnteredByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ExitedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademyId, x.AccessEntitlementId }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.EnteredByUserId, x.EntryKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.ExitedByUserId, x.ExitKey }).IsUnique().HasFilter("\"ExitKey\" IS NOT NULL");
            e.HasIndex(x => new { x.AcademyId, x.PlayerId }).IsUnique().HasFilter("\"ExitedAtUtc\" IS NULL AND \"PlayerId\" IS NOT NULL");
            e.HasIndex(x => new { x.AcademyId, x.GuardianId }).IsUnique().HasFilter("\"ExitedAtUtc\" IS NULL AND \"GuardianId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Visit_Exit", "((\"ExitedAtUtc\" IS NULL AND \"ExitedByUserId\" IS NULL AND \"ExitKey\" IS NULL AND \"ExitHash\" IS NULL) OR (\"ExitedAtUtc\" IS NOT NULL AND \"ExitedAtUtc\">=\"EnteredAtUtc\" AND \"ExitedByUserId\" IS NOT NULL AND \"ExitKey\" IS NOT NULL AND \"ExitHash\" IS NOT NULL)) AND ((\"PersonKind\"=1 AND \"PlayerId\" IS NOT NULL AND \"GuardianId\" IS NULL) OR (\"PersonKind\"=2 AND \"GuardianId\" IS NOT NULL AND \"PlayerId\" IS NULL))"));
        });
    }
}
