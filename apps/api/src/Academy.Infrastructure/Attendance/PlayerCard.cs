using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Attendance;
// Opaque locator only, never an identity, guardian link, payment or attendance grant.
public sealed class PlayerCard : TenantEntity
{
    public Guid PlayerId { get; set; }
    public required string TokenHash { get; set; }
    public required string ProtectedToken { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Guid IssuedByUserId { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public required string RequestKey { get; set; }
    public uint? PreviousVersion { get; set; }
}
