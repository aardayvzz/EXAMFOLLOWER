using LgsTakip.Api.Application.ZoomLessons.Contracts;
using LgsTakip.Api.Application.ZoomLessons.Results;

namespace LgsTakip.Api.Application.ZoomLessons.Interfaces;

public interface IZoomLessonService
{
    Task<ZoomLessonOperationResult<CollectorZoomLessonResponse>> UpsertAsync(
        UpsertZoomLessonRequest request,
        CancellationToken cancellationToken = default);

    Task<ZoomLessonOperationResult<ZoomLessonListResponse>> GetStudentScheduleAsync(
        string? studentIdClaim,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    Task<ZoomLessonOperationResult<IReadOnlyList<ZoomLessonResponse>>> GetStudentCurrentAsync(
        string? studentIdClaim,
        CancellationToken cancellationToken = default);

    Task<ZoomLessonOperationResult<ZoomLessonResponse>> GetStudentByIdAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ZoomLessonOperationResult<ZoomLessonListResponse>> GetParentScheduleAsync(
        string? parentIdClaim,
        Guid studentId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    Task<ZoomLessonOperationResult<IReadOnlyList<ZoomLessonResponse>>> GetParentCurrentAsync(
        string? parentIdClaim,
        Guid studentId,
        CancellationToken cancellationToken = default);
}
