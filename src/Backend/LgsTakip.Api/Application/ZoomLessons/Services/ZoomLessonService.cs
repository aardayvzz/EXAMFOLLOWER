using LgsTakip.Api.Application.ZoomLessons.Contracts;
using LgsTakip.Api.Application.ZoomLessons.Interfaces;
using LgsTakip.Api.Application.ZoomLessons.Results;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LgsTakip.Api.Application.ZoomLessons.Services;

public sealed class ZoomLessonService(
    LgsTakipDbContext dbContext,
    TimeProvider timeProvider) : IZoomLessonService
{
    private const string ExternalIdentityIndexName =
        "UX_ZoomLessons_StudentId_Source_ExternalId";
    private static readonly TimeSpan MaximumLessonDuration =
        TimeSpan.FromHours(8);
    private static readonly TimeSpan MaximumScheduleRange =
        TimeSpan.FromDays(90);

    public async Task<ZoomLessonOperationResult<CollectorZoomLessonResponse>>
        UpsertAsync(
            UpsertZoomLessonRequest request,
            CancellationToken cancellationToken = default)
    {
        var nowUtc = GetUtcNow();
        var validation = ValidateUpsert(request, nowUtc);
        if (!validation.Succeeded)
        {
            return Failure<CollectorZoomLessonResponse>(validation.ErrorCode);
        }

        var normalized = validation.Value!;
        var studentExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == request.StudentId
                && user.Role == UserRole.Student
                && user.IsActive,
                cancellationToken);

        if (!studentExists)
        {
            return Failure<CollectorZoomLessonResponse>(
                ZoomLessonErrorCode.StudentNotFound);
        }

        var existingResult = await FindByExternalIdentityAsync(
            request.StudentId,
            normalized.Source,
            normalized.ExternalId,
            cancellationToken);

        if (existingResult.HasDataIntegrityViolation)
        {
            return Failure<CollectorZoomLessonResponse>(
                ZoomLessonErrorCode.DataIntegrityViolation);
        }

        if (existingResult.Lesson is not null)
        {
            return await UpdateExistingAsync(
                existingResult.Lesson,
                normalized,
                nowUtc,
                cancellationToken);
        }

        var entity = new ZoomLesson
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            Title = normalized.Title,
            StartAtUtc = normalized.StartAtUtc,
            EndAtUtc = normalized.EndAtUtc,
            JoinUrl = normalized.JoinUrl,
            Source = normalized.Source,
            ExternalId = normalized.ExternalId,
            LockRequired = request.LockRequired,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        dbContext.ZoomLessons.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return SuccessCollector(entity, created: true);
        }
        catch (DbUpdateException exception)
            when (IsExternalIdentityDuplicate(exception))
        {
            dbContext.Entry(entity).State = EntityState.Detached;

            var concurrentResult = await FindByExternalIdentityAsync(
                request.StudentId,
                normalized.Source,
                normalized.ExternalId,
                cancellationToken);

            if (concurrentResult.HasDataIntegrityViolation)
            {
                return Failure<CollectorZoomLessonResponse>(
                    ZoomLessonErrorCode.DataIntegrityViolation);
            }

            if (concurrentResult.Lesson is null)
            {
                return Failure<CollectorZoomLessonResponse>(
                    ZoomLessonErrorCode.DataConflict);
            }

            return await UpdateExistingAsync(
                concurrentResult.Lesson,
                normalized,
                nowUtc,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure<CollectorZoomLessonResponse>(
                ZoomLessonErrorCode.DataIntegrityViolation);
        }
    }

    public Task<ZoomLessonOperationResult<ZoomLessonListResponse>>
        GetStudentScheduleAsync(
            string? studentIdClaim,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Task.FromResult(Failure<ZoomLessonListResponse>(
                ZoomLessonErrorCode.InvalidUserIdentity));
        }

        return GetScheduleAsync(
            studentId,
            fromUtc,
            toUtc,
            cancellationToken);
    }

    public Task<ZoomLessonOperationResult<IReadOnlyList<ZoomLessonResponse>>>
        GetStudentCurrentAsync(
            string? studentIdClaim,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Task.FromResult(Failure<
                IReadOnlyList<ZoomLessonResponse>>(
                    ZoomLessonErrorCode.InvalidUserIdentity));
        }

        return GetCurrentAsync(studentId, cancellationToken);
    }

    public async Task<ZoomLessonOperationResult<ZoomLessonResponse>>
        GetStudentByIdAsync(
            string? studentIdClaim,
            Guid id,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<ZoomLessonResponse>(
                ZoomLessonErrorCode.InvalidUserIdentity);
        }

        if (id == Guid.Empty)
        {
            return Failure<ZoomLessonResponse>(
                ZoomLessonErrorCode.InvalidInput);
        }

        var response = await ProjectResponses(
                dbContext.ZoomLessons
                    .AsNoTracking()
                    .Where(lesson => lesson.Id == id
                        && lesson.StudentId == studentId))
            .SingleOrDefaultAsync(cancellationToken);

        return response is null
            ? Failure<ZoomLessonResponse>(ZoomLessonErrorCode.LessonNotFound)
            : ZoomLessonOperationResult<ZoomLessonResponse>.Success(response);
    }

    public async Task<ZoomLessonOperationResult<ZoomLessonListResponse>>
        GetParentScheduleAsync(
            string? parentIdClaim,
            Guid studentId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            CancellationToken cancellationToken = default)
    {
        var access = await ValidateParentAccessAsync(
            parentIdClaim,
            studentId,
            cancellationToken);
        if (access is not ZoomLessonErrorCode.None)
        {
            return Failure<ZoomLessonListResponse>(access);
        }

        return await GetScheduleAsync(
            studentId,
            fromUtc,
            toUtc,
            cancellationToken);
    }

    public async Task<
        ZoomLessonOperationResult<IReadOnlyList<ZoomLessonResponse>>>
        GetParentCurrentAsync(
            string? parentIdClaim,
            Guid studentId,
            CancellationToken cancellationToken = default)
    {
        var access = await ValidateParentAccessAsync(
            parentIdClaim,
            studentId,
            cancellationToken);
        if (access is not ZoomLessonErrorCode.None)
        {
            return Failure<IReadOnlyList<ZoomLessonResponse>>(access);
        }

        return await GetCurrentAsync(studentId, cancellationToken);
    }

    private async Task<ZoomLessonOperationResult<CollectorZoomLessonResponse>>
        UpdateExistingAsync(
            ZoomLesson entity,
            NormalizedUpsert normalized,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
    {
        entity.Title = normalized.Title;
        entity.StartAtUtc = normalized.StartAtUtc;
        entity.EndAtUtc = normalized.EndAtUtc;
        entity.JoinUrl = normalized.JoinUrl;
        entity.LockRequired = normalized.LockRequired;
        entity.UpdatedAtUtc = nowUtc;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure<CollectorZoomLessonResponse>(
                ZoomLessonErrorCode.DataConflict);
        }
        catch (DbUpdateException)
        {
            return Failure<CollectorZoomLessonResponse>(
                ZoomLessonErrorCode.DataIntegrityViolation);
        }

        return SuccessCollector(entity, created: false);
    }

    private async Task<ZoomLessonOperationResult<ZoomLessonListResponse>>
        GetScheduleAsync(
            Guid studentId,
            DateTimeOffset? requestedFromUtc,
            DateTimeOffset? requestedToUtc,
            CancellationToken cancellationToken)
    {
        if (!TryResolveDateRange(
                requestedFromUtc,
                requestedToUtc,
                out var fromUtc,
                out var toUtc))
        {
            return Failure<ZoomLessonListResponse>(
                ZoomLessonErrorCode.InvalidDateRange);
        }

        IReadOnlyList<ZoomLessonResponse> items = await ProjectResponses(
                dbContext.ZoomLessons
                    .AsNoTracking()
                    .Where(lesson => lesson.StudentId == studentId
                        && lesson.StartAtUtc < toUtc
                        && lesson.EndAtUtc > fromUtc)
                    .OrderBy(lesson => lesson.StartAtUtc)
                    .ThenBy(lesson => lesson.Id))
            .ToListAsync(cancellationToken);

        return ZoomLessonOperationResult<ZoomLessonListResponse>.Success(
            new ZoomLessonListResponse
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Items = items
            });
    }

    private async Task<
        ZoomLessonOperationResult<IReadOnlyList<ZoomLessonResponse>>>
        GetCurrentAsync(
            Guid studentId,
            CancellationToken cancellationToken)
    {
        var nowUtc = GetUtcNow();
        IReadOnlyList<ZoomLessonResponse> items = await ProjectResponses(
                dbContext.ZoomLessons
                    .AsNoTracking()
                    .Where(lesson => lesson.StudentId == studentId
                        && lesson.StartAtUtc <= nowUtc
                        && lesson.EndAtUtc > nowUtc)
                    .OrderBy(lesson => lesson.StartAtUtc)
                    .ThenBy(lesson => lesson.Id))
            .ToListAsync(cancellationToken);

        return ZoomLessonOperationResult<
            IReadOnlyList<ZoomLessonResponse>>.Success(items);
    }

    private async Task<ZoomLessonErrorCode> ValidateParentAccessAsync(
        string? parentIdClaim,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return ZoomLessonErrorCode.InvalidUserIdentity;
        }

        if (studentId == Guid.Empty)
        {
            return ZoomLessonErrorCode.InvalidInput;
        }

        var linked = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(student => student.Id == studentId
                && student.Role == UserRole.Student
                && student.Parents.Any(parent => parent.Id == parentId),
                cancellationToken);

        return linked
            ? ZoomLessonErrorCode.None
            : ZoomLessonErrorCode.StudentNotFound;
    }

    private async Task<ExternalIdentityQueryResult>
        FindByExternalIdentityAsync(
            Guid studentId,
            string source,
            string externalId,
            CancellationToken cancellationToken)
    {
        var matches = await dbContext.ZoomLessons
            .Where(lesson => lesson.StudentId == studentId
                && lesson.Source == source
                && lesson.ExternalId == externalId)
            .Take(2)
            .ToListAsync(cancellationToken);

        return matches.Count switch
        {
            0 => new ExternalIdentityQueryResult(false, null),
            1 => new ExternalIdentityQueryResult(false, matches[0]),
            _ => new ExternalIdentityQueryResult(true, null)
        };
    }

    private ZoomLessonOperationResult<NormalizedUpsert> ValidateUpsert(
        UpsertZoomLessonRequest request,
        DateTimeOffset nowUtc)
    {
        if (request.StudentId == Guid.Empty)
        {
            return Failure<NormalizedUpsert>(ZoomLessonErrorCode.InvalidInput);
        }

        var title = request.Title?.Trim() ?? string.Empty;
        var externalId = request.ExternalId?.Trim() ?? string.Empty;
        var joinUrl = request.JoinUrl?.Trim() ?? string.Empty;

        if (title.Length is < 2 or > 200
            || externalId.Length is < 1 or > 200
            || joinUrl.Length is < 1 or > 2048)
        {
            return Failure<NormalizedUpsert>(ZoomLessonErrorCode.InvalidInput);
        }

        if (!TryCanonicalizeSource(request.Source, out var source))
        {
            return Failure<NormalizedUpsert>(
                ZoomLessonErrorCode.UnsupportedSource);
        }

        if (!IsValidZoomUrl(joinUrl))
        {
            return Failure<NormalizedUpsert>(ZoomLessonErrorCode.InvalidZoomUrl);
        }

        var startAtUtc = request.StartAtUtc.ToUniversalTime();
        var endAtUtc = request.EndAtUtc.ToUniversalTime();
        var duration = endAtUtc - startAtUtc;

        if (request.StartAtUtc == default
            || request.EndAtUtc == default
            || duration <= TimeSpan.Zero
            || duration > MaximumLessonDuration
            || startAtUtc < nowUtc.AddDays(-30)
            || startAtUtc > nowUtc.AddYears(1))
        {
            return Failure<NormalizedUpsert>(ZoomLessonErrorCode.InvalidInput);
        }

        return ZoomLessonOperationResult<NormalizedUpsert>.Success(
            new NormalizedUpsert(
                title,
                startAtUtc,
                endAtUtc,
                joinUrl,
                source,
                externalId,
                request.LockRequired));
    }

    private bool TryResolveDateRange(
        DateTimeOffset? requestedFromUtc,
        DateTimeOffset? requestedToUtc,
        out DateTimeOffset fromUtc,
        out DateTimeOffset toUtc)
    {
        fromUtc = (requestedFromUtc ?? GetUtcNow()).ToUniversalTime();
        toUtc = (requestedToUtc ?? fromUtc.AddDays(30)).ToUniversalTime();
        var range = toUtc - fromUtc;
        return range > TimeSpan.Zero && range <= MaximumScheduleRange;
    }

    private static bool TryCanonicalizeSource(
        string? requestedSource,
        out string source)
    {
        var trimmed = requestedSource?.Trim();
        if (string.Equals(trimmed, "Kurspro", StringComparison.OrdinalIgnoreCase))
        {
            source = "Kurspro";
            return true;
        }

        if (string.Equals(trimmed, "Email", StringComparison.OrdinalIgnoreCase))
        {
            source = "Email";
            return true;
        }

        source = string.Empty;
        return false;
    }

    private static bool IsValidZoomUrl(string joinUrl)
    {
        if (!Uri.TryCreate(joinUrl, UriKind.Absolute, out var uri)
            || !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost;
        return string.Equals(host, "zoom.us", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".zoom.us", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalIdentityDuplicate(
        DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627
            && sqlException.Message.Contains(
                ExternalIdentityIndexName,
                StringComparison.Ordinal);
    }

    private static IQueryable<ZoomLessonResponse> ProjectResponses(
        IQueryable<ZoomLesson> query) =>
        query.Select(lesson => new ZoomLessonResponse
        {
            Id = lesson.Id,
            Title = lesson.Title,
            StartAtUtc = lesson.StartAtUtc,
            EndAtUtc = lesson.EndAtUtc,
            JoinUrl = lesson.JoinUrl,
            Source = lesson.Source,
            LockRequired = lesson.LockRequired
        });

    private static ZoomLessonOperationResult<CollectorZoomLessonResponse>
        SuccessCollector(ZoomLesson lesson, bool created) =>
        ZoomLessonOperationResult<CollectorZoomLessonResponse>.Success(
            new CollectorZoomLessonResponse
            {
                Created = created,
                Lesson = new CollectorZoomLessonDetails
                {
                    Id = lesson.Id,
                    StudentId = lesson.StudentId,
                    Title = lesson.Title,
                    StartAtUtc = lesson.StartAtUtc,
                    EndAtUtc = lesson.EndAtUtc,
                    JoinUrl = lesson.JoinUrl,
                    Source = lesson.Source,
                    ExternalId = lesson.ExternalId!,
                    LockRequired = lesson.LockRequired,
                    CreatedAtUtc = lesson.CreatedAtUtc,
                    UpdatedAtUtc = lesson.UpdatedAtUtc
                }
            });

    private DateTimeOffset GetUtcNow() =>
        timeProvider.GetUtcNow().ToUniversalTime();

    private static bool TryGetUserId(string? claim, out Guid userId) =>
        Guid.TryParse(claim, out userId) && userId != Guid.Empty;

    private static ZoomLessonOperationResult<T> Failure<T>(
        ZoomLessonErrorCode errorCode) =>
        ZoomLessonOperationResult<T>.Failure(errorCode);

    private sealed record NormalizedUpsert(
        string Title,
        DateTimeOffset StartAtUtc,
        DateTimeOffset EndAtUtc,
        string JoinUrl,
        string Source,
        string ExternalId,
        bool LockRequired);

    private sealed record ExternalIdentityQueryResult(
        bool HasDataIntegrityViolation,
        ZoomLesson? Lesson);
}
