using LgsTakip.Api.Application.StudySessions.Contracts;
using LgsTakip.Api.Application.StudySessions.Results;
using LgsTakip.Api.Domain.Enums;

namespace LgsTakip.Api.Application.StudySessions.Interfaces;

public interface IStudySessionService
{
    Task<StudySessionResult<StudySessionResponse>> StartAsync(
        string? studentIdClaim,
        StartStudySessionRequest request,
        CancellationToken cancellationToken = default);

    Task<StudySessionResult<StudySessionResponse>> GetByIdAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StudySessionResult<StudySessionResponse>> GetActiveAsync(
        string? studentIdClaim,
        CancellationToken cancellationToken = default);

    Task<StudySessionResult<StudySessionResponse>> CompleteAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StudySessionResult<StudySessionResponse>> CancelAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StudySessionResult<StudySessionListResponse>> GetHistoryAsync(
        string? studentIdClaim,
        int page,
        int pageSize,
        StudySessionStatus? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);
}
