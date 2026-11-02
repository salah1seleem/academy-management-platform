using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Facilities;
public enum EntryPersonKind { Participant = 1, Companion = 2 }

// Immutable admission decision. Neither a payment nor attendance consumption.
public sealed class AccessEntitlement : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ResourceId { get; set; }
    public Guid ResourceBookingId { get; set; }
    public EntryPersonKind PersonKind { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid? SportEnrollmentId { get; set; }
    public Guid? GuardianId { get; set; }
    public Guid? SubscriptionPeriodId { get; set; }
    public Guid? CompensationBookingId { get; set; }
    public Guid? DayPassTicketId { get; set; }
    public Guid? AccompanyingVisitId { get; set; }
    public DateOnly BookingDate { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset ValidToUtc { get; set; }
    public Guid AuthorizedByUserId { get; set; }
    public required string Basis { get; set; }
}
public sealed class EntryVisit : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ResourceId { get; set; }
    public Guid ResourceBookingId { get; set; }
    public Guid AccessEntitlementId { get; set; }
    public EntryPersonKind PersonKind { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid? GuardianId { get; set; }
    public DateOnly BookingDate { get; set; }
    public DateTimeOffset EnteredAtUtc { get; set; }
    public Guid EnteredByUserId { get; set; }
    public required string EntryKey { get; set; }
    public required string EntryHash { get; set; }
    public DateTimeOffset? ExitedAtUtc { get; set; }
    public Guid? ExitedByUserId { get; set; }
    public string? ExitKey { get; set; }
    public string? ExitHash { get; set; }
}
public static class VisitWindow
{
    public static (DateTimeOffset From, DateTimeOffset To) For(DateOnly day, TimeOnly start, TimeOnly end, TimeZoneInfo zone)
    {
        var from = day.ToDateTime(start, DateTimeKind.Unspecified).AddMinutes(-15);
        var to = day.ToDateTime(end, DateTimeKind.Unspecified);
        if (start >= end || zone.IsInvalidTime(from) || zone.IsAmbiguousTime(from) || zone.IsInvalidTime(to) || zone.IsAmbiguousTime(to)) throw new ArgumentException("توقيت الحجز يحتاج مراجعة.");
        return (new(TimeZoneInfo.ConvertTimeToUtc(from, zone)), new(TimeZoneInfo.ConvertTimeToUtc(to, zone)));
    }
    public static bool Contains(DateTimeOffset now, DateTimeOffset from, DateTimeOffset to) => now >= from && now < to;
}
