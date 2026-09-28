using Microsoft.AspNetCore.Identity;

namespace Academy.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
