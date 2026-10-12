namespace LgsTakip.Api.Application.ZoomLessons.Contracts;

public sealed class CollectorZoomLessonResponse
{
    public bool Created { get; init; }
    public CollectorZoomLessonDetails Lesson { get; init; } = new();
}

public sealed class CollectorZoomLessonDetails
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset StartAtUtc { get; init; }
    public DateTimeOffset EndAtUtc { get; init; }
    public string JoinUrl { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public bool LockRequired { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
