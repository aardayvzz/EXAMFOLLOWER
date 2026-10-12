namespace LgsTakip.Api.Application.ParentAnalytics.Contracts;

public sealed class ActiveStudySessionResponse
{
    public Guid Id { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public int PlannedDurationMinutes { get; init; }
    public long ElapsedSeconds { get; init; }
}
