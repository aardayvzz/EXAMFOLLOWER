namespace LgsTakip.Api.Domain.Entities;

public class ExamResult
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public User Student { get; set; } = null!;
    public string ExamName { get; set; } = string.Empty;
    public DateTimeOffset ExamDateUtc { get; set; }
    public int CorrectCount { get; set; }
    public int IncorrectCount { get; set; }
    public int BlankCount { get; set; }
    public decimal NetScore { get; set; }
    public decimal? LgsScore { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
