namespace LgsTakip.Api.Application.ExamResults.Contracts;

public sealed class ExamResultResponse
{
    public Guid Id { get; init; }
    public string ExamName { get; init; } = string.Empty;
    public DateTimeOffset ExamDateUtc { get; init; }
    public int CorrectCount { get; init; }
    public int IncorrectCount { get; init; }
    public int BlankCount { get; init; }
    public int TotalQuestions { get; init; }
    public decimal NetScore { get; init; }
    public decimal? LgsScore { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
