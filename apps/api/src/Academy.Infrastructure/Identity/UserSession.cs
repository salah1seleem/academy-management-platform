namespace Academy.Infrastructure.Identity;

public sealed class UserSession
{
    public required string Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ActiveAcademyId { get; set; }
    public required byte[] Ticket { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public ApplicationUser User { get; set; } = null!;
}
