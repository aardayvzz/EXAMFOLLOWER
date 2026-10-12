using LgsTakip.Api.Domain.Enums;

namespace LgsTakip.Api.Domain.Entities;

public class StudySession
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public User Student { get; set; } = null!;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public int PlannedDurationMinutes { get; set; }
    public int CompletedDurationSeconds { get; set; }
    public StudySessionStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
