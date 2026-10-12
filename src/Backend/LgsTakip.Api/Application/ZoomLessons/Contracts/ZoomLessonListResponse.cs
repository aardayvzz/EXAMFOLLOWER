namespace LgsTakip.Api.Application.ZoomLessons.Contracts;

public sealed class ZoomLessonListResponse
{
    public DateTimeOffset FromUtc { get; init; }
    public DateTimeOffset ToUtc { get; init; }
    public IReadOnlyList<ZoomLessonResponse> Items { get; init; } = [];
}
