using LgsTakip.Api.Application.StudySessions.Contracts;
using LgsTakip.Api.Application.StudySessions.Interfaces;
using LgsTakip.Api.Application.StudySessions.Results;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LgsTakip.Api.Application.StudySessions.Services;

public sealed class StudySessionService(
    LgsTakipDbContext dbContext,
    TimeProvider timeProvider) : IStudySessionService
{
    private const string RunningSessionIndexName =
        "UX_StudySessions_StudentId_Running";

    public async Task<StudySessionResult<StudySessionResponse>> StartAsync(
        string? studentIdClaim,
        StartStudySessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetStudentId(studentIdClaim, out var studentId))
        {
            return Failure(StudySessionErrorCode.InvalidUserIdentity);
        }

        if (request is null
            || request.PlannedDurationMinutes is < 5 or > 180)
        {
            return Failure(StudySessionErrorCode.InvalidInput);
        }

        var activeSessionExists = await dbContext.StudySessions.AnyAsync(
            session => session.StudentId == studentId
                && session.Status == StudySessionStatus.Running,
            cancellationToken);

        if (activeSessionExists)
        {
            return Failure(StudySessionErrorCode.ActiveSessionAlreadyExists);
        }

        var nowUtc = GetUtcNow();
        var session = new StudySession
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            StartedAtUtc = nowUtc,
            EndedAtUtc = null,
            PlannedDurationMinutes = request.PlannedDurationMinutes,
            CompletedDurationSeconds = 0,
            Status = StudySessionStatus.Running,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        dbContext.StudySessions.Add(session);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsRunningSessionUniqueIndexViolation(exception))
        {
            dbContext.Entry(session).State = EntityState.Detached;
            return Failure(StudySessionErrorCode.ActiveSessionAlreadyExists);
        }

        return Success(ToResponse(session));
    }

    public async Task<StudySessionResult<StudySessionResponse>> GetByIdAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetStudentId(studentIdClaim, out var studentId))
        {
            return Failure(StudySessionErrorCode.InvalidUserIdentity);
        }

        var session = await dbContext.StudySessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id
                    && candidate.StudentId == studentId,
                cancellationToken);

        return session is null
            ? Failure(StudySessionErrorCode.NotFound)
            : Success(ToResponse(session));
    }

    public async Task<StudySessionResult<StudySessionResponse>> GetActiveAsync(
        string? studentIdClaim,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetStudentId(studentIdClaim, out var studentId))
        {
            return Failure(StudySessionErrorCode.InvalidUserIdentity);
        }

        var activeSessions = await dbContext.StudySessions
            .AsNoTracking()
            .Where(session => session.StudentId == studentId
                && session.Status == StudySessionStatus.Running)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (activeSessions.Count > 1)
        {
            return Failure(StudySessionErrorCode.DataIntegrityViolation);
        }

        return Success(activeSessions.Count == 0
            ? null
            : ToResponse(activeSessions[0]));
    }

    public Task<StudySessionResult<StudySessionResponse>> CompleteAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return FinishAsync(
            studentIdClaim,
            id,
            StudySessionStatus.Completed,
            cancellationToken);
    }

    public Task<StudySessionResult<StudySessionResponse>> CancelAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return FinishAsync(
            studentIdClaim,
            id,
            StudySessionStatus.Cancelled,
            cancellationToken);
    }

    public async Task<StudySessionResult<StudySessionListResponse>> GetHistoryAsync(
        string? studentIdClaim,
        int page,
        int pageSize,
        StudySessionStatus? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetStudentId(studentIdClaim, out var studentId))
        {
            return StudySessionResult<StudySessionListResponse>.Failure(
                StudySessionErrorCode.InvalidUserIdentity);
        }

        if (page < 1
            || pageSize is < 1 or > 100
            || status.HasValue && !Enum.IsDefined(status.Value)
            || fromUtc.HasValue && toUtc.HasValue && fromUtc >= toUtc)
        {
            return StudySessionResult<StudySessionListResponse>.Failure(
                StudySessionErrorCode.InvalidInput);
        }

        var normalizedFromUtc = fromUtc?.ToUniversalTime();
        var normalizedToUtc = toUtc?.ToUniversalTime();

        var query = dbContext.StudySessions
            .AsNoTracking()
            .Where(session => session.StudentId == studentId);

        if (status.HasValue)
        {
            query = query.Where(session => session.Status == status.Value);
        }

        if (normalizedFromUtc.HasValue)
        {
            query = query.Where(
                session => session.StartedAtUtc >= normalizedFromUtc.Value);
        }

        if (normalizedToUtc.HasValue)
        {
            query = query.Where(
                session => session.StartedAtUtc < normalizedToUtc.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = (long)(page - 1) * pageSize;
        IReadOnlyList<StudySessionResponse> items;

        if (skip >= totalCount)
        {
            items = [];
        }
        else
        {
            items = await query
                .OrderByDescending(session => session.StartedAtUtc)
                .ThenByDescending(session => session.Id)
                .Skip((int)skip)
                .Take(pageSize)
                .Select(session => new StudySessionResponse
                {
                    Id = session.Id,
                    Status = session.Status,
                    StartedAtUtc = session.StartedAtUtc,
                    EndedAtUtc = session.EndedAtUtc,
                    PlannedDurationMinutes = session.PlannedDurationMinutes,
                    CompletedDurationSeconds = session.CompletedDurationSeconds
                })
                .ToListAsync(cancellationToken);
        }

        return StudySessionResult<StudySessionListResponse>.Success(
            new StudySessionListResponse
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            });
    }

    private async Task<StudySessionResult<StudySessionResponse>> FinishAsync(
        string? studentIdClaim,
        Guid id,
        StudySessionStatus targetStatus,
        CancellationToken cancellationToken)
    {
        if (!TryGetStudentId(studentIdClaim, out var studentId))
        {
            return Failure(StudySessionErrorCode.InvalidUserIdentity);
        }

        var session = await dbContext.StudySessions.SingleOrDefaultAsync(
            candidate => candidate.Id == id
                && candidate.StudentId == studentId,
            cancellationToken);

        if (session is null)
        {
            return Failure(StudySessionErrorCode.NotFound);
        }

        if (session.Status == targetStatus)
        {
            return Success(ToResponse(session));
        }

        if (session.Status != StudySessionStatus.Running)
        {
            return Failure(StudySessionErrorCode.InvalidStateTransition);
        }

        var endedAtUtc = GetUtcNow();
        session.EndedAtUtc = endedAtUtc;
        session.CompletedDurationSeconds = CalculateElapsedSeconds(
            session.StartedAtUtc,
            endedAtUtc);
        session.Status = targetStatus;
        session.UpdatedAtUtc = endedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Success(ToResponse(session));
    }

    private DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow().ToUniversalTime();
    }

    private static int CalculateElapsedSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc)
    {
        var elapsedTicks = (endedAtUtc - startedAtUtc).Ticks;

        if (elapsedTicks <= 0)
        {
            return 0;
        }

        var elapsedSeconds = elapsedTicks / TimeSpan.TicksPerSecond;
        return elapsedSeconds >= int.MaxValue
            ? int.MaxValue
            : (int)elapsedSeconds;
    }

    private static bool TryGetStudentId(
        string? studentIdClaim,
        out Guid studentId)
    {
        return Guid.TryParse(studentIdClaim, out studentId)
            && studentId != Guid.Empty;
    }

    private static bool IsRunningSessionUniqueIndexViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is SqlException
            {
                Number: 2601 or 2627
            } sqlException
            && sqlException.Message.Contains(
                RunningSessionIndexName,
                StringComparison.Ordinal);
    }

    private static StudySessionResponse ToResponse(StudySession session)
    {
        return new StudySessionResponse
        {
            Id = session.Id,
            Status = session.Status,
            StartedAtUtc = session.StartedAtUtc,
            EndedAtUtc = session.EndedAtUtc,
            PlannedDurationMinutes = session.PlannedDurationMinutes,
            CompletedDurationSeconds = session.CompletedDurationSeconds
        };
    }

    private static StudySessionResult<StudySessionResponse> Success(
        StudySessionResponse? response)
    {
        return StudySessionResult<StudySessionResponse>.Success(response);
    }

    private static StudySessionResult<StudySessionResponse> Failure(
        StudySessionErrorCode errorCode)
    {
        return StudySessionResult<StudySessionResponse>.Failure(errorCode);
    }
}
