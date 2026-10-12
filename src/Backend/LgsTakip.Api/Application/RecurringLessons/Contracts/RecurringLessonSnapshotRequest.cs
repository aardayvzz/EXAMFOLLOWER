using System.ComponentModel.DataAnnotations;

namespace LgsTakip.Api.Application.RecurringLessons.Contracts;

public sealed class RecurringLessonSnapshotRequest
{
    public Guid StudentId { get; init; }

    [Required, StringLength(50)]
    public string Source { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string TimeZoneId { get; init; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; init; }
    public bool IsCompleteSnapshot { get; init; }

    [Required, MinLength(1), MaxLength(100)]
    public IReadOnlyList<RecurringLessonSnapshotItemRequest> Lessons { get; init; } = [];
}

public sealed class RecurringLessonSnapshotItemRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string ExternalId { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 2)]
    public string Title { get; init; } = string.Empty;

    [Range(1, 7)]
    public int IsoDayOfWeek { get; init; }

    public TimeOnly StartLocalTime { get; init; }
    public TimeOnly EndLocalTime { get; init; }
    public bool NotificationEnabled { get; init; } = true;
    public int ReminderMinutesBefore { get; init; }
    public bool LockRequired { get; init; } = true;
}
