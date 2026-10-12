using System.ComponentModel.DataAnnotations;

namespace LgsTakip.Api.Application.ExamResults.Contracts;

public sealed class UpdateExamResultRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string ExamName { get; init; } = string.Empty;

    public DateTimeOffset ExamDateUtc { get; init; }

    [Range(0, 90)]
    public int CorrectCount { get; init; }

    [Range(0, 90)]
    public int IncorrectCount { get; init; }

    [Range(0, 90)]
    public int BlankCount { get; init; }

    [Range(typeof(decimal), "0", "500")]
    public decimal? LgsScore { get; init; }
}
