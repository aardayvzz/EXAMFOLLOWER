namespace LgsTakip.Api.Domain.Entities;

public class ZoomLesson
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public User Student { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset StartAtUtc { get; set; }
    public DateTimeOffset EndAtUtc { get; set; }
    public string JoinUrl { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public bool LockRequired { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
