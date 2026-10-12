namespace LgsTakip.Api.Application.ParentAnalytics.Contracts;

public sealed class ParentStudentStudyStatusResponse
{
    public Guid StudentId { get; init; }
    public bool IsStudying { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }
    public ActiveStudySessionResponse? ActiveSession { get; init; }
}
