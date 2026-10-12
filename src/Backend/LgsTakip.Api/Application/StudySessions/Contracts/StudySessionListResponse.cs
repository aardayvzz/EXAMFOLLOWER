namespace LgsTakip.Api.Application.StudySessions.Contracts;

public sealed class StudySessionListResponse
{
    public required IReadOnlyList<StudySessionResponse> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
}
