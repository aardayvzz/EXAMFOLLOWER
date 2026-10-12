namespace LgsTakip.Api.Application.RecurringLessons.Contracts;

public sealed class ParentRecurringLessonResponse
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public int IsoDayOfWeek { get; init; }
    public TimeOnly StartLocalTime { get; init; }
    public TimeOnly EndLocalTime { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsStale { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; init; }
    public bool NotificationEnabled { get; init; }
    public int ReminderMinutesBefore { get; init; }
    public bool LockRequired { get; init; }
}
