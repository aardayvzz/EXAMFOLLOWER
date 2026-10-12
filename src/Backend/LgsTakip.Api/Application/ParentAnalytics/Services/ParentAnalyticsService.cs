using LgsTakip.Api.Application.ParentAnalytics.Contracts;
using LgsTakip.Api.Application.ParentAnalytics.Interfaces;
using LgsTakip.Api.Application.ParentAnalytics.Results;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgsTakip.Api.Application.ParentAnalytics.Services;

public sealed class ParentAnalyticsService(
    LgsTakipDbContext dbContext,
    TimeProvider timeProvider) : IParentAnalyticsService
{
    private const string DefaultTimeZoneId = "Europe/Istanbul";
    private static readonly TimeSpan MaximumStatisticsRange =
        TimeSpan.FromDays(93);

    public async Task<
        ParentAnalyticsResult<IReadOnlyList<LinkedStudentResponse>>>
        GetLinkedStudentsAsync(
            string? parentIdClaim,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return ParentAnalyticsResult<
                IReadOnlyList<LinkedStudentResponse>>.Failure(
                    ParentAnalyticsErrorCode.InvalidUserIdentity);
        }

        IReadOnlyList<LinkedStudentResponse> students = await dbContext.Users
            .AsNoTracking()
            .Where(student => student.Role == UserRole.Student
                && student.Parents.Any(parent => parent.Id == parentId))
            .OrderBy(student => student.DisplayName)
            .ThenBy(student => student.Id)
            .Select(student => new LinkedStudentResponse
            {
                Id = student.Id,
                DisplayName = student.DisplayName,
                Email = student.Email,
                IsActive = student.IsActive
            })
            .ToListAsync(cancellationToken);

        return ParentAnalyticsResult<
            IReadOnlyList<LinkedStudentResponse>>.Success(students);
    }

    public async Task<ParentAnalyticsResult<ParentStudyStatisticsResponse>>
        GetStudyStatisticsAsync(
            string? parentIdClaim,
            Guid studentId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? timeZoneId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return ParentAnalyticsResult<ParentStudyStatisticsResponse>.Failure(
                ParentAnalyticsErrorCode.InvalidUserIdentity);
        }

        if (studentId == Guid.Empty)
        {
            return ParentAnalyticsResult<ParentStudyStatisticsResponse>.Failure(
                ParentAnalyticsErrorCode.InvalidInput);
        }

        if (!TryResolveTimeZone(
                timeZoneId,
                out var timeZone,
                out var responseTimeZoneId))
        {
            return ParentAnalyticsResult<ParentStudyStatisticsResponse>.Failure(
                ParentAnalyticsErrorCode.InvalidTimeZone);
        }

        var generatedAtUtc = GetUtcNow();
        if (!TryResolveDateRange(
                fromUtc,
                toUtc,
                generatedAtUtc,
                timeZone,
                out var resolvedFromUtc,
                out var resolvedToUtc))
        {
            return ParentAnalyticsResult<ParentStudyStatisticsResponse>.Failure(
                ParentAnalyticsErrorCode.InvalidDateRange);
        }

        var student = await GetLinkedStudentAsync(
            parentId,
            studentId,
            cancellationToken);

        if (student is null)
        {
            return ParentAnalyticsResult<ParentStudyStatisticsResponse>.Failure(
                ParentAnalyticsErrorCode.StudentNotFound);
        }

        var sessions = await dbContext.StudySessions
            .AsNoTracking()
            .Where(session => session.StudentId == studentId
                && session.StartedAtUtc >= resolvedFromUtc
                && session.StartedAtUtc < resolvedToUtc
                && (session.Status == StudySessionStatus.Completed
                    || session.Status == StudySessionStatus.Cancelled))
            .Select(session => new SessionStatisticsRow
            {
                Status = session.Status,
                StartedAtUtc = session.StartedAtUtc,
                PlannedDurationMinutes = session.PlannedDurationMinutes,
                CompletedDurationSeconds = session.CompletedDurationSeconds
            })
            .ToListAsync(cancellationToken);

        var activeResult = await GetActiveSessionAsync(
            studentId,
            generatedAtUtc,
            cancellationToken);

        if (activeResult.HasDataIntegrityViolation)
        {
            return ParentAnalyticsResult<ParentStudyStatisticsResponse>.Failure(
                ParentAnalyticsErrorCode.DataIntegrityViolation);
        }

        var response = new ParentStudyStatisticsResponse
        {
            Student = student,
            FromUtc = resolvedFromUtc,
            ToUtc = resolvedToUtc,
            TimeZoneId = responseTimeZoneId,
            GeneratedAtUtc = generatedAtUtc,
            Summary = CreateSummary(sessions),
            Daily = CreateDailyStatistics(
                sessions,
                resolvedFromUtc,
                resolvedToUtc,
                timeZone),
            ActiveSession = activeResult.Session
        };

        return ParentAnalyticsResult<ParentStudyStatisticsResponse>.Success(
            response);
    }

    public async Task<
        ParentAnalyticsResult<ParentStudentStudyStatusResponse>>
        GetStudyStatusAsync(
            string? parentIdClaim,
            Guid studentId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return ParentAnalyticsResult<
                ParentStudentStudyStatusResponse>.Failure(
                    ParentAnalyticsErrorCode.InvalidUserIdentity);
        }

        if (studentId == Guid.Empty)
        {
            return ParentAnalyticsResult<
                ParentStudentStudyStatusResponse>.Failure(
                    ParentAnalyticsErrorCode.InvalidInput);
        }

        var studentExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(student => student.Id == studentId
                && student.Role == UserRole.Student
                && student.Parents.Any(parent => parent.Id == parentId),
                cancellationToken);

        if (!studentExists)
        {
            return ParentAnalyticsResult<
                ParentStudentStudyStatusResponse>.Failure(
                    ParentAnalyticsErrorCode.StudentNotFound);
        }

        var checkedAtUtc = GetUtcNow();
        var activeResult = await GetActiveSessionAsync(
            studentId,
            checkedAtUtc,
            cancellationToken);

        if (activeResult.HasDataIntegrityViolation)
        {
            return ParentAnalyticsResult<
                ParentStudentStudyStatusResponse>.Failure(
                    ParentAnalyticsErrorCode.DataIntegrityViolation);
        }

        return ParentAnalyticsResult<
            ParentStudentStudyStatusResponse>.Success(
                new ParentStudentStudyStatusResponse
                {
                    StudentId = studentId,
                    IsStudying = activeResult.Session is not null,
                    CheckedAtUtc = checkedAtUtc,
                    ActiveSession = activeResult.Session
                });
    }

    private async Task<LinkedStudentResponse?> GetLinkedStudentAsync(
        Guid parentId,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(student => student.Id == studentId
                && student.Role == UserRole.Student
                && student.Parents.Any(parent => parent.Id == parentId))
            .Select(student => new LinkedStudentResponse
            {
                Id = student.Id,
                DisplayName = student.DisplayName,
                Email = student.Email,
                IsActive = student.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<ActiveSessionQueryResult> GetActiveSessionAsync(
        Guid studentId,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.StudySessions
            .AsNoTracking()
            .Where(session => session.StudentId == studentId
                && session.Status == StudySessionStatus.Running)
            .Take(2)
            .Select(session => new ActiveSessionRow
            {
                Id = session.Id,
                StartedAtUtc = session.StartedAtUtc,
                PlannedDurationMinutes = session.PlannedDurationMinutes
            })
            .ToListAsync(cancellationToken);

        if (sessions.Count > 1)
        {
            return new ActiveSessionQueryResult(true, null);
        }

        if (sessions.Count == 0)
        {
            return new ActiveSessionQueryResult(false, null);
        }

        var session = sessions[0];
        return new ActiveSessionQueryResult(
            false,
            new ActiveStudySessionResponse
            {
                Id = session.Id,
                StartedAtUtc = session.StartedAtUtc,
                PlannedDurationMinutes = session.PlannedDurationMinutes,
                ElapsedSeconds = CalculateElapsedSeconds(
                    session.StartedAtUtc,
                    generatedAtUtc)
            });
    }

    private static StudyStatisticsSummaryResponse CreateSummary(
        IReadOnlyCollection<SessionStatisticsRow> sessions)
    {
        var completedSessions = sessions
            .Where(session => session.Status == StudySessionStatus.Completed)
            .ToArray();
        var completedCount = completedSessions.Length;
        var cancelledCount = sessions.Count - completedCount;
        var totalCount = completedCount + cancelledCount;
        var completedStudySeconds = completedSessions.Sum(
            session => (long)session.CompletedDurationSeconds);

        var averageCompletedSeconds = completedCount == 0
            ? 0m
            : decimal.Round(
                (decimal)completedStudySeconds / completedCount,
                2,
                MidpointRounding.AwayFromZero);
        var completionRate = totalCount == 0
            ? 0m
            : decimal.Round(
                completedCount * 100m / totalCount,
                2,
                MidpointRounding.AwayFromZero);

        return new StudyStatisticsSummaryResponse
        {
            TotalSessionCount = totalCount,
            CompletedSessionCount = completedCount,
            CancelledSessionCount = cancelledCount,
            TotalStudySeconds = sessions.Sum(
                session => (long)session.CompletedDurationSeconds),
            PlannedStudyMinutes = sessions.Sum(
                session => (long)session.PlannedDurationMinutes),
            AverageCompletedSessionSeconds = averageCompletedSeconds,
            CompletionRatePercent = completionRate
        };
    }

    private static IReadOnlyList<DailyStudyStatisticsResponse>
        CreateDailyStatistics(
            IReadOnlyCollection<SessionStatisticsRow> sessions,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            TimeZoneInfo timeZone)
    {
        var totalsByDate = new Dictionary<DateOnly, DailyAccumulator>();

        foreach (var session in sessions)
        {
            var localStartedAt = TimeZoneInfo.ConvertTime(
                session.StartedAtUtc,
                timeZone);
            var date = DateOnly.FromDateTime(localStartedAt.DateTime);

            if (!totalsByDate.TryGetValue(date, out var accumulator))
            {
                accumulator = new DailyAccumulator();
                totalsByDate.Add(date, accumulator);
            }

            accumulator.TotalSessionCount++;
            accumulator.TotalStudySeconds += session.CompletedDurationSeconds;
            accumulator.PlannedStudyMinutes += session.PlannedDurationMinutes;

            if (session.Status == StudySessionStatus.Completed)
            {
                accumulator.CompletedSessionCount++;
            }
            else
            {
                accumulator.CancelledSessionCount++;
            }
        }

        var firstDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(fromUtc, timeZone).DateTime);
        var lastDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(toUtc.AddTicks(-1), timeZone).DateTime);
        var daily = new List<DailyStudyStatisticsResponse>();

        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            totalsByDate.TryGetValue(date, out var accumulator);
            daily.Add(new DailyStudyStatisticsResponse
            {
                Date = date,
                TotalSessionCount = accumulator?.TotalSessionCount ?? 0,
                CompletedSessionCount =
                    accumulator?.CompletedSessionCount ?? 0,
                CancelledSessionCount =
                    accumulator?.CancelledSessionCount ?? 0,
                TotalStudySeconds = accumulator?.TotalStudySeconds ?? 0,
                PlannedStudyMinutes = accumulator?.PlannedStudyMinutes ?? 0
            });
        }

        return daily;
    }

    private static bool TryResolveDateRange(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone,
        out DateTimeOffset resolvedFromUtc,
        out DateTimeOffset resolvedToUtc)
    {
        if (fromUtc.HasValue != toUtc.HasValue)
        {
            resolvedFromUtc = default;
            resolvedToUtc = default;
            return false;
        }

        if (!fromUtc.HasValue)
        {
            var localToday = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(generatedAtUtc, timeZone).DateTime);
            resolvedFromUtc = ConvertLocalDateStartToUtc(
                localToday.AddDays(-6),
                timeZone);
            resolvedToUtc = ConvertLocalDateStartToUtc(
                localToday.AddDays(1),
                timeZone);
            return true;
        }

        resolvedFromUtc = fromUtc.Value.ToUniversalTime();
        resolvedToUtc = toUtc!.Value.ToUniversalTime();
        var duration = resolvedToUtc - resolvedFromUtc;

        return duration > TimeSpan.Zero
            && duration <= MaximumStatisticsRange;
    }

    private static DateTimeOffset ConvertLocalDateStartToUtc(
        DateOnly date,
        TimeZoneInfo timeZone)
    {
        var localDateTime = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);

        while (timeZone.IsInvalidTime(localDateTime))
        {
            localDateTime = localDateTime.AddMinutes(1);
        }

        var offset = timeZone.IsAmbiguousTime(localDateTime)
            ? timeZone.GetAmbiguousTimeOffsets(localDateTime).Max()
            : timeZone.GetUtcOffset(localDateTime);

        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }

    private static bool TryResolveTimeZone(
        string? requestedTimeZoneId,
        out TimeZoneInfo timeZone,
        out string responseTimeZoneId)
    {
        var candidateId = requestedTimeZoneId is null
            ? DefaultTimeZoneId
            : requestedTimeZoneId.Trim();

        if (string.IsNullOrWhiteSpace(candidateId))
        {
            timeZone = null!;
            responseTimeZoneId = string.Empty;
            return false;
        }

        if (TryFindTimeZone(candidateId, out timeZone))
        {
            responseTimeZoneId = candidateId;
            return true;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(
                candidateId,
                out var windowsId)
            && TryFindTimeZone(windowsId, out timeZone))
        {
            responseTimeZoneId = candidateId;
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(
                candidateId,
                out var ianaId)
            && TryFindTimeZone(ianaId, out timeZone))
        {
            responseTimeZoneId = candidateId;
            return true;
        }

        timeZone = null!;
        responseTimeZoneId = string.Empty;
        return false;
    }

    private static bool TryFindTimeZone(
        string timeZoneId,
        out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = null!;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = null!;
            return false;
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow().ToUniversalTime();
    }

    private static bool TryGetUserId(
        string? userIdClaim,
        out Guid userId)
    {
        return Guid.TryParse(userIdClaim, out userId)
            && userId != Guid.Empty;
    }

    private static long CalculateElapsedSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset generatedAtUtc)
    {
        var elapsedTicks = (generatedAtUtc - startedAtUtc).Ticks;
        return elapsedTicks <= 0
            ? 0
            : elapsedTicks / TimeSpan.TicksPerSecond;
    }

    private sealed class SessionStatisticsRow
    {
        public StudySessionStatus Status { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public int PlannedDurationMinutes { get; init; }
        public int CompletedDurationSeconds { get; init; }
    }

    private sealed class ActiveSessionRow
    {
        public Guid Id { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public int PlannedDurationMinutes { get; init; }
    }

    private sealed class DailyAccumulator
    {
        public int TotalSessionCount { get; set; }
        public int CompletedSessionCount { get; set; }
        public int CancelledSessionCount { get; set; }
        public long TotalStudySeconds { get; set; }
        public long PlannedStudyMinutes { get; set; }
    }

    private sealed record ActiveSessionQueryResult(
        bool HasDataIntegrityViolation,
        ActiveStudySessionResponse? Session);
}
