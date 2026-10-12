using System.ComponentModel.DataAnnotations;

namespace LgsTakip.Api.Application.ZoomLessons.Contracts;

public sealed class UpsertZoomLessonRequest
{
    public Guid StudentId { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string Title { get; init; } = string.Empty;

    public DateTimeOffset StartAtUtc { get; init; }
    public DateTimeOffset EndAtUtc { get; init; }

    [Required]
    [StringLength(2048)]
    public string JoinUrl { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Source { get; init; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string ExternalId { get; init; } = string.Empty;

    public bool LockRequired { get; init; }
}
