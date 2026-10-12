namespace LgsTakip.Api.Application.ExamResults.Contracts;

public sealed class ExamResultListResponse
{
    public IReadOnlyList<ExamResultResponse> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
}
