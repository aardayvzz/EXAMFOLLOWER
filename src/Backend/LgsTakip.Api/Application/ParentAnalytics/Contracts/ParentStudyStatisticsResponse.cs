namespace LgsTakip.Api.Application.ParentAnalytics.Contracts;

public sealed class ParentStudyStatisticsResponse
{
    public required LinkedStudentResponse Student { get; init; }
    public DateTimeOffset FromUtc { get; init; }
    public DateTimeOffset ToUtc { get; init; }
    public required string TimeZoneId { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public required StudyStatisticsSummaryResponse Summary { get; init; }
    public required IReadOnlyList<DailyStudyStatisticsResponse> Daily { get; init; }
    public ActiveStudySessionResponse? ActiveSession { get; init; }
}
