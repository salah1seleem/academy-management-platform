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
    public DbSet<DayPassTicket> DayPassTickets => Set<DayPassTicket>();
    private static void ConfigureVisits(ModelBuilder b)
    {
        Tenant<AccessEntitlement>(b); Tenant<EntryVisit>(b); Tenant<DayPassTicket>(b);
        b.Entity<DayPassTicket>(e => {
            e.Property(x => x.PlanName).HasMaxLength(160);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Amount).HasPrecision(18,2);
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.Property(x => x.ReviewReason).HasMaxLength(500);
            e.HasOne<ResourceBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceBookingId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademyId, x.RequestedByUserId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.ResourceBookingId, x.Status });
            e.ToTable(t => t.HasCheckConstraint("CK_DayPass_Amount", "\"Amount\">0 AND \"PlanRevision\">0 AND \"Status\" BETWEEN 1 AND 4 AND ((\"Status\" IN (1,2) AND \"ReviewedByUserId\" IS NULL AND \"ReviewedAtUtc\" IS NULL) OR (\"Status\" IN (3,4) AND \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL))"));
        });
        b.Entity<AccessEntitlement>(e => {
            e.Property(x => x.Basis).HasMaxLength(60);
            e.HasOne<ResourceBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceBookingId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FacilityResource>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GuardianProfile>().WithMany().HasForeignKey(x => new { x.AcademyId, x.GuardianId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPeriodId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CompensationBooking>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CompensationBookingId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DayPassTicket>().WithMany().HasForeignKey(x => new { x.AcademyId, x.DayPassTicketId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EntryVisit>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AccompanyingVisitId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorizedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademyId, x.DayPassTicketId }).IsUnique().HasFilter("\"DayPassTicketId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Access_Kind", "\"ValidFromUtc\"<\"ValidToUtc\" AND ((\"PersonKind\"=1 AND \"PlayerId\" IS NOT NULL AND \"GuardianId\" IS NULL AND \"AccompanyingVisitId\" IS NULL AND ((\"SportEnrollmentId\" IS NOT NULL AND \"DayPassTicketId\" IS NULL AND ((\"SubscriptionPeriodId\" IS NOT NULL)::int+(\"CompensationBookingId\" IS NOT NULL)::int)=1) OR (\"SportEnrollmentId\" IS NULL AND \"DayPassTicketId\" IS NOT NULL AND \"SubscriptionPeriodId\" IS NULL AND \"CompensationBookingId\" IS NULL))) OR (\"PersonKind\"=2 AND \"PlayerId\" IS NULL AND \"SportEnrollmentId\" IS NULL AND \"DayPassTicketId\" IS NULL AND \"GuardianId\" IS NOT NULL AND \"AccompanyingVisitId\" IS NOT NULL AND \"SubscriptionPeriodId\" IS NULL AND \"CompensationBookingId\" IS NULL))"));
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
