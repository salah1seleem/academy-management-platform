namespace Academy.Infrastructure.Tenancy;

public sealed class Academy
{
    public Guid Id { get; set; }
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;
    public required string TimeZone { get; set; }
    public required string DefaultCurrency { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public uint Version { get; set; }
}
