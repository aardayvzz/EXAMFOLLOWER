using LgsTakip.Api.Application.ExamResults.Contracts;
using LgsTakip.Api.Application.ExamResults.Interfaces;
using LgsTakip.Api.Application.ExamResults.Results;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgsTakip.Api.Application.ExamResults.Services;

public sealed class ExamResultService(
    LgsTakipDbContext dbContext,
    TimeProvider timeProvider) : IExamResultService
{
    private const int MaximumPageSize = 100;
    private const int MaximumQuestionCount = 90;
    private static readonly TimeSpan MaximumDateRange = TimeSpan.FromDays(730);
    private static readonly TimeSpan FutureDateTolerance = TimeSpan.FromMinutes(5);

    public async Task<ExamResultOperationResult<ExamResultResponse>> CreateAsync(
        string? studentIdClaim,
        CreateExamResultRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.InvalidUserIdentity);
        }

        var nowUtc = GetUtcNow();
        if (!TryValidateInput(
                request.ExamName,
                request.ExamDateUtc,
                request.CorrectCount,
                request.IncorrectCount,
                request.BlankCount,
                request.LgsScore,
                nowUtc,
                out var examName,
                out var examDateUtc))
        {
            return Failure<ExamResultResponse>(ExamResultErrorCode.InvalidInput);
        }

        var studentExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == studentId
                && user.Role == UserRole.Student,
                cancellationToken);

        if (!studentExists)
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.StudentNotFound);
        }

        var entity = new ExamResult
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            ExamName = examName,
            ExamDateUtc = examDateUtc,
            CorrectCount = request.CorrectCount,
            IncorrectCount = request.IncorrectCount,
            BlankCount = request.BlankCount,
            NetScore = CalculateNetScore(
                request.CorrectCount,
                request.IncorrectCount),
            LgsScore = request.LgsScore,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        dbContext.ExamResults.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.DataIntegrityViolation);
        }

        return ExamResultOperationResult<ExamResultResponse>.Success(
            ToResponse(entity));
    }

    public async Task<ExamResultOperationResult<ExamResultResponse>> GetByIdAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.InvalidUserIdentity);
        }

        if (id == Guid.Empty)
        {
            return Failure<ExamResultResponse>(ExamResultErrorCode.InvalidInput);
        }

        var response = await ProjectResponses(
                dbContext.ExamResults.AsNoTracking()
                    .Where(result => result.Id == id
                        && result.StudentId == studentId))
            .SingleOrDefaultAsync(cancellationToken);

        return response is null
            ? Failure<ExamResultResponse>(ExamResultErrorCode.NotFound)
            : ExamResultOperationResult<ExamResultResponse>.Success(response);
    }

    public Task<ExamResultOperationResult<ExamResultListResponse>> GetListAsync(
        string? studentIdClaim,
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Task.FromResult(Failure<ExamResultListResponse>(
                ExamResultErrorCode.InvalidUserIdentity));
        }

        return GetListCoreAsync(
            studentId,
            page,
            pageSize,
            fromUtc,
            toUtc,
            cancellationToken);
    }

    public async Task<ExamResultOperationResult<ExamResultResponse>> UpdateAsync(
        string? studentIdClaim,
        Guid id,
        UpdateExamResultRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.InvalidUserIdentity);
        }

        var nowUtc = GetUtcNow();
        if (id == Guid.Empty
            || !TryValidateInput(
                request.ExamName,
                request.ExamDateUtc,
                request.CorrectCount,
                request.IncorrectCount,
                request.BlankCount,
                request.LgsScore,
                nowUtc,
                out var examName,
                out var examDateUtc))
        {
            return Failure<ExamResultResponse>(ExamResultErrorCode.InvalidInput);
        }

        var entity = await dbContext.ExamResults
            .SingleOrDefaultAsync(result => result.Id == id
                && result.StudentId == studentId,
                cancellationToken);

        if (entity is null)
        {
            return Failure<ExamResultResponse>(ExamResultErrorCode.NotFound);
        }

        entity.ExamName = examName;
        entity.ExamDateUtc = examDateUtc;
        entity.CorrectCount = request.CorrectCount;
        entity.IncorrectCount = request.IncorrectCount;
        entity.BlankCount = request.BlankCount;
        entity.NetScore = CalculateNetScore(
            request.CorrectCount,
            request.IncorrectCount);
        entity.LgsScore = request.LgsScore;
        entity.UpdatedAtUtc = nowUtc;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.DataIntegrityViolation);
        }

        return ExamResultOperationResult<ExamResultResponse>.Success(
            ToResponse(entity));
    }

    public async Task<ExamResultOperationResult<bool>> DeleteAsync(
        string? studentIdClaim,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<bool>(ExamResultErrorCode.InvalidUserIdentity);
        }

        if (id == Guid.Empty)
        {
            return Failure<bool>(ExamResultErrorCode.InvalidInput);
        }

        var entity = await dbContext.ExamResults
            .SingleOrDefaultAsync(result => result.Id == id
                && result.StudentId == studentId,
                cancellationToken);

        if (entity is null)
        {
            return Failure<bool>(ExamResultErrorCode.NotFound);
        }

        dbContext.ExamResults.Remove(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure<bool>(ExamResultErrorCode.DataIntegrityViolation);
        }

        return ExamResultOperationResult<bool>.Success(true);
    }

    public async Task<ExamResultOperationResult<ExamResultListResponse>>
        GetParentListAsync(
            string? parentIdClaim,
            Guid studentId,
            int page,
            int pageSize,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return Failure<ExamResultListResponse>(
                ExamResultErrorCode.InvalidUserIdentity);
        }

        if (studentId == Guid.Empty)
        {
            return Failure<ExamResultListResponse>(
                ExamResultErrorCode.InvalidInput);
        }

        if (!await IsLinkedStudentAsync(
                parentId,
                studentId,
                cancellationToken))
        {
            return Failure<ExamResultListResponse>(
                ExamResultErrorCode.StudentNotFound);
        }

        return await GetListCoreAsync(
            studentId,
            page,
            pageSize,
            fromUtc,
            toUtc,
            cancellationToken);
    }

    public async Task<ExamResultOperationResult<ExamResultResponse>>
        GetParentByIdAsync(
            string? parentIdClaim,
            Guid studentId,
            Guid examResultId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.InvalidUserIdentity);
        }

        if (studentId == Guid.Empty || examResultId == Guid.Empty)
        {
            return Failure<ExamResultResponse>(ExamResultErrorCode.InvalidInput);
        }

        if (!await IsLinkedStudentAsync(
                parentId,
                studentId,
                cancellationToken))
        {
            return Failure<ExamResultResponse>(
                ExamResultErrorCode.StudentNotFound);
        }

        var response = await ProjectResponses(
                dbContext.ExamResults.AsNoTracking()
                    .Where(result => result.Id == examResultId
                        && result.StudentId == studentId))
            .SingleOrDefaultAsync(cancellationToken);

        return response is null
            ? Failure<ExamResultResponse>(ExamResultErrorCode.NotFound)
            : ExamResultOperationResult<ExamResultResponse>.Success(response);
    }

    private async Task<ExamResultOperationResult<ExamResultListResponse>>
        GetListCoreAsync(
            Guid studentId,
            int page,
            int pageSize,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
        {
            return Failure<ExamResultListResponse>(
                ExamResultErrorCode.InvalidInput);
        }

        if (!TryNormalizeDateRange(
                fromUtc,
                toUtc,
                out var normalizedFromUtc,
                out var normalizedToUtc))
        {
            return Failure<ExamResultListResponse>(
                ExamResultErrorCode.InvalidDateRange);
        }

        var query = dbContext.ExamResults
            .AsNoTracking()
            .Where(result => result.StudentId == studentId);

        if (normalizedFromUtc.HasValue)
        {
            query = query.Where(result =>
                result.ExamDateUtc >= normalizedFromUtc.Value);
        }

        if (normalizedToUtc.HasValue)
        {
            query = query.Where(result =>
                result.ExamDateUtc < normalizedToUtc.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = ((long)page - 1) * pageSize;
        IReadOnlyList<ExamResultResponse> items;

        if (skip >= totalCount || skip > int.MaxValue)
        {
            items = [];
        }
        else
        {
            items = await ProjectResponses(query
                    .OrderByDescending(result => result.ExamDateUtc)
                    .ThenBy(result => result.Id)
                    .Skip((int)skip)
                    .Take(pageSize))
                .ToListAsync(cancellationToken);
        }

        return ExamResultOperationResult<ExamResultListResponse>.Success(
            new ExamResultListResponse
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            });
    }

    private async Task<bool> IsLinkedStudentAsync(
        Guid parentId,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .AnyAsync(student => student.Id == studentId
                && student.Role == UserRole.Student
                && student.Parents.Any(parent => parent.Id == parentId),
                cancellationToken);
    }

    private static IQueryable<ExamResultResponse> ProjectResponses(
        IQueryable<ExamResult> query)
    {
        return query.Select(result => new ExamResultResponse
        {
            Id = result.Id,
            ExamName = result.ExamName,
            ExamDateUtc = result.ExamDateUtc,
            CorrectCount = result.CorrectCount,
            IncorrectCount = result.IncorrectCount,
            BlankCount = result.BlankCount,
            TotalQuestions = result.CorrectCount
                + result.IncorrectCount
                + result.BlankCount,
            NetScore = result.NetScore,
            LgsScore = result.LgsScore,
            CreatedAtUtc = result.CreatedAtUtc,
            UpdatedAtUtc = result.UpdatedAtUtc
        });
    }

    private static ExamResultResponse ToResponse(ExamResult result)
    {
        return new ExamResultResponse
        {
            Id = result.Id,
            ExamName = result.ExamName,
            ExamDateUtc = result.ExamDateUtc,
            CorrectCount = result.CorrectCount,
            IncorrectCount = result.IncorrectCount,
            BlankCount = result.BlankCount,
            TotalQuestions = result.CorrectCount
                + result.IncorrectCount
                + result.BlankCount,
            NetScore = result.NetScore,
            LgsScore = result.LgsScore,
            CreatedAtUtc = result.CreatedAtUtc,
            UpdatedAtUtc = result.UpdatedAtUtc
        };
    }

    private static bool TryValidateInput(
        string? requestedExamName,
        DateTimeOffset requestedExamDateUtc,
        int correctCount,
        int incorrectCount,
        int blankCount,
        decimal? lgsScore,
        DateTimeOffset nowUtc,
        out string examName,
        out DateTimeOffset examDateUtc)
    {
        examName = requestedExamName?.Trim() ?? string.Empty;
        examDateUtc = requestedExamDateUtc.ToUniversalTime();
        var totalQuestions = (long)correctCount + incorrectCount + blankCount;

        return examName.Length is >= 2 and <= 150
            && requestedExamDateUtc != default
            && examDateUtc <= nowUtc + FutureDateTolerance
            && correctCount >= 0
            && incorrectCount >= 0
            && blankCount >= 0
            && totalQuestions is >= 1 and <= MaximumQuestionCount
            && (!lgsScore.HasValue
                || lgsScore.Value is >= 0m and <= 500m);
    }

    private static bool TryNormalizeDateRange(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        out DateTimeOffset? normalizedFromUtc,
        out DateTimeOffset? normalizedToUtc)
    {
        normalizedFromUtc = fromUtc?.ToUniversalTime();
        normalizedToUtc = toUtc?.ToUniversalTime();

        if (!normalizedFromUtc.HasValue || !normalizedToUtc.HasValue)
        {
            return true;
        }

        var range = normalizedToUtc.Value - normalizedFromUtc.Value;
        return range > TimeSpan.Zero && range <= MaximumDateRange;
    }

    private static decimal CalculateNetScore(
        int correctCount,
        int incorrectCount)
    {
        return decimal.Round(
            correctCount - (incorrectCount / 3m),
            2,
            MidpointRounding.AwayFromZero);
    }

    private DateTimeOffset GetUtcNow() =>
        timeProvider.GetUtcNow().ToUniversalTime();

    private static bool TryGetUserId(string? claim, out Guid userId) =>
        Guid.TryParse(claim, out userId) && userId != Guid.Empty;

    private static ExamResultOperationResult<T> Failure<T>(
        ExamResultErrorCode errorCode) =>
        ExamResultOperationResult<T>.Failure(errorCode);
}
