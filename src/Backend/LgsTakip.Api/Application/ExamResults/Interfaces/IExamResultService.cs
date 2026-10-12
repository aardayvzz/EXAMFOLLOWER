using LgsTakip.Api.Application.ExamResults.Contracts;
using LgsTakip.Api.Application.ExamResults.Results;

namespace LgsTakip.Api.Application.ExamResults.Interfaces;

public interface IExamResultService
{
    Task<ExamResultOperationResult<ExamResultResponse>> CreateAsync(
        string? studentIdClaim,
        CreateExamResultRequest request,
        CancellationToken cancellationToken = default);

    Task<ExamResultOperationResult<ExamResultResponse>> GetByIdAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ExamResultOperationResult<ExamResultListResponse>> GetListAsync(
        string? studentIdClaim,
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    Task<ExamResultOperationResult<ExamResultResponse>> UpdateAsync(
        string? studentIdClaim,
        Guid id,
        UpdateExamResultRequest request,
        CancellationToken cancellationToken = default);

    Task<ExamResultOperationResult<bool>> DeleteAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ExamResultOperationResult<ExamResultListResponse>> GetParentListAsync(
        string? parentIdClaim,
        Guid studentId,
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    Task<ExamResultOperationResult<ExamResultResponse>> GetParentByIdAsync(
        string? parentIdClaim,
        Guid studentId,
        Guid examResultId,
        CancellationToken cancellationToken = default);
}
