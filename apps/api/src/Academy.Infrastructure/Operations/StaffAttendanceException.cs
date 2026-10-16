using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// A claim and its independent administrative decision, not device attestation.
public sealed class StaffAttendanceException : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid BranchId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public required string Operation { get; set; }
    public DateTimeOffset ClaimedAtUtc { get; set; }
    public DateOnly WorkDate { get; set; }
    public required string Reason { get; set; }
    public string Status { get; set; } = "Pending";
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public Guid? ReviewedByMembershipId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReviewKey { get; set; }
    public string? ReviewHash { get; set; }
    public Guid? AttendanceId { get; set; }
    public string? BeforeSnapshot { get; set; }
    public string? AfterSnapshot { get; set; }
}
