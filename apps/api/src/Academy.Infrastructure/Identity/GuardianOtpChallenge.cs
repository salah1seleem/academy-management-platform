namespace Academy.Infrastructure.Identity;

public sealed class GuardianOtpChallenge
{
    public Guid Id { get; set; }
    public required string PhoneNumberNormalized { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
}
