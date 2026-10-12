namespace LgsTakip.Api.Application.ZoomLessons.Contracts;

public sealed class ZoomLessonResponse
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset StartAtUtc { get; init; }
    public DateTimeOffset EndAtUtc { get; init; }
    public string JoinUrl { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool LockRequired { get; init; }
}
