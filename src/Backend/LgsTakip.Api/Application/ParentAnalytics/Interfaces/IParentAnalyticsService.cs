using LgsTakip.Api.Application.ParentAnalytics.Contracts;
using LgsTakip.Api.Application.ParentAnalytics.Results;

namespace LgsTakip.Api.Application.ParentAnalytics.Interfaces;

public interface IParentAnalyticsService
{
    Task<ParentAnalyticsResult<IReadOnlyList<LinkedStudentResponse>>>
        GetLinkedStudentsAsync(
            string? parentIdClaim,
            CancellationToken cancellationToken = default);

    Task<ParentAnalyticsResult<ParentStudyStatisticsResponse>>
        GetStudyStatisticsAsync(
            string? parentIdClaim,
            Guid studentId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? timeZoneId,
            CancellationToken cancellationToken = default);

    Task<ParentAnalyticsResult<ParentStudentStudyStatusResponse>>
        GetStudyStatusAsync(
            string? parentIdClaim,
            Guid studentId,
            CancellationToken cancellationToken = default);
}
