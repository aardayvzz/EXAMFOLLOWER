namespace LgsTakip.Api.Application.RecurringLessons.Contracts;

public sealed class LessonNotificationScheduleResponse
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public DateTimeOffset ValidUntilUtc { get; init; }
    public IReadOnlyList<LessonNotificationOccurrenceResponse> Items { get; init; } = [];
}

public sealed class LessonNotificationOccurrenceResponse
{
    public string OccurrenceId { get; init; } = string.Empty;
    public Guid ScheduleId { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset StartsAtUtc { get; init; }
    public DateTimeOffset EndsAtUtc { get; init; }
    public DateTimeOffset ReminderAtUtc { get; init; }
    public bool LockRequired { get; init; }
    public string Source { get; init; } = string.Empty;
}
