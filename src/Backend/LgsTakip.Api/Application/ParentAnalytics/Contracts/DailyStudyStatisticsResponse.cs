namespace LgsTakip.Api.Application.ParentAnalytics.Contracts;

public sealed class DailyStudyStatisticsResponse
{
    public DateOnly Date { get; init; }
    public int TotalSessionCount { get; init; }
    public int CompletedSessionCount { get; init; }
    public int CancelledSessionCount { get; init; }
    public long TotalStudySeconds { get; init; }
    public long PlannedStudyMinutes { get; init; }
}
