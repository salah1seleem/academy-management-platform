namespace Academy.Infrastructure.Identity;

public sealed class PlatformMfaUse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Fingerprint { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}

// A platform grant is not an AcademyMembership or an academy owner role.
public sealed class PlatformAdministrator
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedMfaAttempts { get; set; }
    public DateTimeOffset? MfaLockedUntilUtc { get; set; }
    public string? LastMfaFingerprint { get; set; }
    public DateTimeOffset? LastMfaAtUtc { get; set; }
}

public sealed class PlatformAudit
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? TargetAcademyId { get; set; }
    public required string Action { get; set; }
    public required string Details { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}
