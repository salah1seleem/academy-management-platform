namespace Academy.Infrastructure.Content;

// Platform-owned instructional media. Not academy content or a child's PrivateAsset.
public sealed class HelpVideoRevision
{
    public Guid Id { get; set; }
    public required string LessonId { get; set; }
    public int Revision { get; set; }
    public int EditVersion { get; set; } = 1;
    public string State { get; set; } = "Draft";
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string AlternativeText { get; set; }
    public string? VideoFile { get; set; }
    public string? ThumbnailFile { get; set; }
    public int? DurationSeconds { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}
