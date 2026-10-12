using LgsTakip.Api.Domain.Enums;

namespace LgsTakip.Api.Application.StudySessions.Contracts;

public sealed class StudySessionResponse
{
    public Guid Id { get; init; }
    public StudySessionStatus Status { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public int PlannedDurationMinutes { get; init; }
    public int CompletedDurationSeconds { get; init; }
}
