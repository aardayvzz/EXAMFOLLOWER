using LgsTakip.Api.Application.RecurringLessons.Contracts;
using LgsTakip.Api.Application.RecurringLessons.Results;

namespace LgsTakip.Api.Application.RecurringLessons.Interfaces;

public interface IRecurringLessonService
{
    Task<RecurringLessonResult<RecurringLessonSnapshotResponse>> ProcessSnapshotAsync(
        RecurringLessonSnapshotRequest request,
        CancellationToken cancellationToken = default);

    Task<RecurringLessonResult<LessonNotificationScheduleResponse>> GetUpcomingAsync(
        string? studentIdClaim,
        int days,
        CancellationToken cancellationToken = default);

    Task<RecurringLessonResult<IReadOnlyList<ParentRecurringLessonResponse>>> GetForParentAsync(
        string? parentIdClaim,
        Guid studentId,
        CancellationToken cancellationToken = default);
}
