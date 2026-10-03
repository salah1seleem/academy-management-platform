using Academy.Infrastructure.Tenancy;

namespace Academy.Infrastructure.Identity;

public sealed class MobileSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? MembershipId { get; set; }
    public required string AccessTokenHash { get; set; }
    public required string RefreshTokenHash { get; set; }
    public required string SecurityStamp { get; set; }
    public required string DeviceName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastUsedAtUtc { get; set; }
    public DateTimeOffset AccessExpiresAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public AcademyMembership? Membership { get; set; }
}

// Deliberately separate from browser OTP challenges: a challenge has one purpose.
public sealed class MobileOtpChallenge
{
    public Guid Id { get; set; }
    public required string PhoneNumberNormalized { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
}
