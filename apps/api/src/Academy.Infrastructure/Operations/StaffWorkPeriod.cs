using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// Preserved intervals under the existing single daily financial source.
public sealed class StaffWorkPeriod : TenantEntity
{
    public Guid AttendanceId { get; set; }
    public Guid MembershipId { get; set; }
    public DateOnly StartDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public DateOnly? EndDate { get; set; }
    public TimeOnly? EndTime { get; set; }
    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public string? TimeZoneId { get; set; }
    public string TrustSource { get; set; } = "AdminDeclared";
    public Guid RecordedByUserId { get; set; }
}
