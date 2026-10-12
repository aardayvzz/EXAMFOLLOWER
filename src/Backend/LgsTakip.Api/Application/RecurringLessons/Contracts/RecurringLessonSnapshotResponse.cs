namespace LgsTakip.Api.Application.RecurringLessons.Contracts;

public sealed class RecurringLessonSnapshotResponse
{
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int ReactivatedCount { get; init; }
    public int DeactivatedCount { get; init; }
    public int UnchangedCount { get; init; }
    public DateTimeOffset ProcessedAtUtc { get; init; }
}
