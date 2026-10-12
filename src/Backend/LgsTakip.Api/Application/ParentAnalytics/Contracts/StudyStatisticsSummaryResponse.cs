namespace LgsTakip.Api.Application.ParentAnalytics.Contracts;

public sealed class StudyStatisticsSummaryResponse
{
    public int TotalSessionCount { get; init; }
    public int CompletedSessionCount { get; init; }
    public int CancelledSessionCount { get; init; }
    public long TotalStudySeconds { get; init; }
    public long PlannedStudyMinutes { get; init; }
    public decimal AverageCompletedSessionSeconds { get; init; }
    public decimal CompletionRatePercent { get; init; }
}
