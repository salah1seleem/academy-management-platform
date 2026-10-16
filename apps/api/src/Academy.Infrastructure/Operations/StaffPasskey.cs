using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// Operational verification only, not a login credential or proof of attendance.
public sealed class StaffPasskey : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required string CredentialHash { get; set; }
    public required byte[] CredentialId { get; set; }
    public required byte[] PublicKey { get; set; }
    public required byte[] UserHandle { get; set; }
    public long SignCount { get; set; }
    public bool BackupEligible { get; set; }
    public bool BackedUp { get; set; }
    public required string RpId { get; set; }
    public Guid RegistrationChallengeId { get; set; }
    public DateTimeOffset? LastVerifiedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class StaffPasskeyChallenge : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid UserId { get; set; }
    public required string Purpose { get; set; }
    public required string Name { get; set; }
    public required string OptionsJson { get; set; }
    public required string RpId { get; set; }
    public required string OriginsJson { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? OperationJson { get; set; }
    public string? ResultJson { get; set; }
}
