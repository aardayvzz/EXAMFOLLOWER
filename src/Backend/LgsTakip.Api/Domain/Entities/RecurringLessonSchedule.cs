namespace LgsTakip.Api.Domain.Entities;

public sealed class RecurringLessonSchedule
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public User Student { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public int IsoDayOfWeek { get; set; }
    public TimeOnly StartLocalTime { get; set; }
    public TimeOnly EndLocalTime { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public bool NotificationEnabled { get; set; } = true;
    public int ReminderMinutesBefore { get; set; }
    public bool LockRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
