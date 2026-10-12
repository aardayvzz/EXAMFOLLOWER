using System.Security.Cryptography;
using System.Text;
using LgsTakip.Api.Application.RecurringLessons.Contracts;
using LgsTakip.Api.Application.RecurringLessons.Interfaces;
using LgsTakip.Api.Application.RecurringLessons.Results;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgsTakip.Api.Application.RecurringLessons.Services;

public sealed class RecurringLessonService(
    LgsTakipDbContext dbContext,
    TimeProvider timeProvider) : IRecurringLessonService
{
    private const string KursproSource = "Kurspro";
    private const string IstanbulTimeZone = "Europe/Istanbul";
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromDays(8);
    private static readonly HashSet<int> AllowedReminders = [0, 5, 10, 15, 30, 60];

    public async Task<RecurringLessonResult<RecurringLessonSnapshotResponse>> ProcessSnapshotAsync(
        RecurringLessonSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = ValidateSnapshot(request);
        if (!normalized.Succeeded || normalized.Value is null)
        {
            return Failure<RecurringLessonSnapshotResponse>(normalized.ErrorCode);
        }

        var studentExists = await dbContext.Users.AsNoTracking().AnyAsync(
            user => user.Id == request.StudentId
                && user.Role == UserRole.Student
                && user.IsActive,
            cancellationToken);
        if (!studentExists)
        {
            return Failure<RecurringLessonSnapshotResponse>(RecurringLessonErrorCode.StudentNotFound);
        }

        var processedAtUtc = GetUtcNow();
        var collectedAtUtc = request.CollectedAtUtc.ToUniversalTime();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await dbContext.RecurringLessonSchedules
                .Where(schedule => schedule.StudentId == request.StudentId
                    && schedule.Source == KursproSource)
                .ToListAsync(cancellationToken);
            var byExternalId = existing.ToDictionary(schedule => schedule.ExternalId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var created = 0;
            var updated = 0;
            var reactivated = 0;
            var unchanged = 0;

            foreach (var item in normalized.Value)
            {
                seen.Add(item.ExternalId);
                if (!byExternalId.TryGetValue(item.ExternalId, out var entity))
                {
                    dbContext.RecurringLessonSchedules.Add(new RecurringLessonSchedule
                    {
                        Id = Guid.NewGuid(),
                        StudentId = request.StudentId,
                        Title = item.Title,
                        IsoDayOfWeek = item.IsoDayOfWeek,
                        StartLocalTime = item.StartLocalTime,
                        EndLocalTime = item.EndLocalTime,
                        TimeZoneId = IstanbulTimeZone,
                        Source = KursproSource,
                        ExternalId = item.ExternalId,
                        NotificationEnabled = item.NotificationEnabled,
                        ReminderMinutesBefore = item.ReminderMinutesBefore,
                        LockRequired = item.LockRequired,
                        IsActive = true,
                        LastSeenAtUtc = collectedAtUtc,
                        CreatedAtUtc = processedAtUtc,
                        UpdatedAtUtc = processedAtUtc
                    });
                    created++;
                    continue;
                }

                var contentChanged = entity.Title != item.Title
                    || entity.IsoDayOfWeek != item.IsoDayOfWeek
                    || entity.StartLocalTime != item.StartLocalTime
                    || entity.EndLocalTime != item.EndLocalTime
                    || entity.TimeZoneId != IstanbulTimeZone
                    || entity.NotificationEnabled != item.NotificationEnabled
                    || entity.ReminderMinutesBefore != item.ReminderMinutesBefore
                    || entity.LockRequired != item.LockRequired;
                var wasInactive = !entity.IsActive;

                entity.Title = item.Title;
                entity.IsoDayOfWeek = item.IsoDayOfWeek;
                entity.StartLocalTime = item.StartLocalTime;
                entity.EndLocalTime = item.EndLocalTime;
                entity.TimeZoneId = IstanbulTimeZone;
                entity.NotificationEnabled = item.NotificationEnabled;
                entity.ReminderMinutesBefore = item.ReminderMinutesBefore;
                entity.LockRequired = item.LockRequired;
                entity.IsActive = true;
                entity.LastSeenAtUtc = collectedAtUtc;
                entity.UpdatedAtUtc = processedAtUtc;

                if (wasInactive)
                {
                    reactivated++;
                }
                else if (contentChanged)
                {
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }

            var deactivated = 0;
            if (request.IsCompleteSnapshot)
            {
                foreach (var missing in existing.Where(schedule => schedule.IsActive && !seen.Contains(schedule.ExternalId)))
                {
                    missing.IsActive = false;
                    missing.UpdatedAtUtc = processedAtUtc;
                    deactivated++;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RecurringLessonResult<RecurringLessonSnapshotResponse>.Success(new()
            {
                CreatedCount = created,
                UpdatedCount = updated,
                ReactivatedCount = reactivated,
                DeactivatedCount = deactivated,
                UnchangedCount = unchanged,
                ProcessedAtUtc = processedAtUtc
            });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure<RecurringLessonSnapshotResponse>(RecurringLessonErrorCode.DataIntegrityViolation);
        }
    }

    public async Task<RecurringLessonResult<LessonNotificationScheduleResponse>> GetUpcomingAsync(
        string? studentIdClaim,
        int days,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<LessonNotificationScheduleResponse>(RecurringLessonErrorCode.InvalidUserIdentity);
        }
        if (days is < 1 or > 30)
        {
            return Failure<LessonNotificationScheduleResponse>(RecurringLessonErrorCode.InvalidInput);
        }

        var generatedAtUtc = GetUtcNow();
        var staleBeforeUtc = generatedAtUtc - FreshnessWindow;
        var schedules = await dbContext.RecurringLessonSchedules.AsNoTracking()
            .Where(schedule => schedule.StudentId == studentId
                && schedule.IsActive
                && schedule.NotificationEnabled
                && schedule.LastSeenAtUtc >= staleBeforeUtc)
            .Select(schedule => new ScheduleProjection(
                schedule.Id,
                schedule.Title,
                schedule.IsoDayOfWeek,
                schedule.StartLocalTime,
                schedule.EndLocalTime,
                schedule.TimeZoneId,
                schedule.Source,
                schedule.ReminderMinutesBefore,
                schedule.LockRequired,
                schedule.LastSeenAtUtc))
            .ToListAsync(cancellationToken);

        var rangeEndUtc = generatedAtUtc.AddDays(days);
        var items = new List<LessonNotificationOccurrenceResponse>();
        foreach (var schedule in schedules)
        {
            if (!TryResolveTimeZone(schedule.TimeZoneId, out var timeZone))
            {
                return Failure<LessonNotificationScheduleResponse>(RecurringLessonErrorCode.DataIntegrityViolation);
            }

            var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(generatedAtUtc, timeZone).DateTime);
            var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(rangeEndUtc, timeZone).DateTime);
            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                if (ToIsoDayOfWeek(date.DayOfWeek) != schedule.IsoDayOfWeek
                    || !TryConvertToUtc(date, schedule.StartLocalTime, timeZone, out var startsAtUtc)
                    || !TryConvertToUtc(date, schedule.EndLocalTime, timeZone, out var endsAtUtc)
                    || startsAtUtc < generatedAtUtc
                    || startsAtUtc >= rangeEndUtc)
                {
                    continue;
                }

                items.Add(new LessonNotificationOccurrenceResponse
                {
                    OccurrenceId = CreateOccurrenceId(schedule.Id, date),
                    ScheduleId = schedule.Id,
                    Title = schedule.Title,
                    StartsAtUtc = startsAtUtc,
                    EndsAtUtc = endsAtUtc,
                    ReminderAtUtc = startsAtUtc.AddMinutes(-schedule.ReminderMinutesBefore),
                    LockRequired = schedule.LockRequired,
                    Source = schedule.Source
                });
            }
        }

        var validUntilUtc = schedules.Count == 0
            ? rangeEndUtc
            : schedules.Select(schedule => schedule.LastSeenAtUtc + FreshnessWindow)
                .Append(rangeEndUtc)
                .Min();
        return RecurringLessonResult<LessonNotificationScheduleResponse>.Success(new()
        {
            GeneratedAtUtc = generatedAtUtc,
            ValidUntilUtc = validUntilUtc,
            Items = items.OrderBy(item => item.ReminderAtUtc).ThenBy(item => item.ScheduleId).ToList()
        });
    }

    public async Task<RecurringLessonResult<IReadOnlyList<ParentRecurringLessonResponse>>> GetForParentAsync(
        string? parentIdClaim,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return Failure<IReadOnlyList<ParentRecurringLessonResponse>>(RecurringLessonErrorCode.InvalidUserIdentity);
        }
        if (studentId == Guid.Empty)
        {
            return Failure<IReadOnlyList<ParentRecurringLessonResponse>>(RecurringLessonErrorCode.InvalidInput);
        }

        var linked = await dbContext.Users.AsNoTracking().AnyAsync(
            student => student.Id == studentId
                && student.Role == UserRole.Student
                && student.Parents.Any(parent => parent.Id == parentId),
            cancellationToken);
        if (!linked)
        {
            return Failure<IReadOnlyList<ParentRecurringLessonResponse>>(RecurringLessonErrorCode.StudentNotFound);
        }

        var staleBeforeUtc = GetUtcNow() - FreshnessWindow;
        IReadOnlyList<ParentRecurringLessonResponse> response = await dbContext.RecurringLessonSchedules
            .AsNoTracking()
            .Where(schedule => schedule.StudentId == studentId)
            .OrderBy(schedule => schedule.IsoDayOfWeek)
            .ThenBy(schedule => schedule.StartLocalTime)
            .ThenBy(schedule => schedule.Id)
            .Select(schedule => new ParentRecurringLessonResponse
            {
                Id = schedule.Id,
                Title = schedule.Title,
                IsoDayOfWeek = schedule.IsoDayOfWeek,
                StartLocalTime = schedule.StartLocalTime,
                EndLocalTime = schedule.EndLocalTime,
                TimeZoneId = schedule.TimeZoneId,
                Source = schedule.Source,
                IsActive = schedule.IsActive,
                IsStale = schedule.LastSeenAtUtc < staleBeforeUtc,
                LastSeenAtUtc = schedule.LastSeenAtUtc,
                NotificationEnabled = schedule.NotificationEnabled,
                ReminderMinutesBefore = schedule.ReminderMinutesBefore,
                LockRequired = schedule.LockRequired
            })
            .ToListAsync(cancellationToken);
        return RecurringLessonResult<IReadOnlyList<ParentRecurringLessonResponse>>.Success(response);
    }

    private static RecurringLessonResult<IReadOnlyList<NormalizedItem>> ValidateSnapshot(
        RecurringLessonSnapshotRequest request)
    {
        if (request.StudentId == Guid.Empty || request.CollectedAtUtc == default
            || request.Lessons is null || request.Lessons.Count is < 1 or > 100)
        {
            return Failure<IReadOnlyList<NormalizedItem>>(RecurringLessonErrorCode.InvalidInput);
        }
        if (!string.Equals(request.Source, KursproSource, StringComparison.Ordinal))
        {
            return Failure<IReadOnlyList<NormalizedItem>>(RecurringLessonErrorCode.UnsupportedSource);
        }
        if (!string.Equals(request.TimeZoneId, IstanbulTimeZone, StringComparison.Ordinal)
            || !TryResolveTimeZone(request.TimeZoneId, out _))
        {
            return Failure<IReadOnlyList<NormalizedItem>>(RecurringLessonErrorCode.InvalidTimeZone);
        }

        var normalized = new List<NormalizedItem>(request.Lessons.Count);
        var externalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Lessons)
        {
            var title = item.Title?.Trim() ?? string.Empty;
            var externalId = item.ExternalId?.Trim() ?? string.Empty;
            var duration = item.EndLocalTime - item.StartLocalTime;
            if (title.Length is < 2 or > 200 || externalId.Length is < 1 or > 200
                || item.IsoDayOfWeek is < 1 or > 7
                || duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(8)
                || !AllowedReminders.Contains(item.ReminderMinutesBefore)
                || !externalIds.Add(externalId))
            {
                return Failure<IReadOnlyList<NormalizedItem>>(RecurringLessonErrorCode.InvalidInput);
            }
            normalized.Add(new(externalId, title, item.IsoDayOfWeek, item.StartLocalTime,
                item.EndLocalTime, item.NotificationEnabled, item.ReminderMinutesBefore,
                item.LockRequired));
        }
        return RecurringLessonResult<IReadOnlyList<NormalizedItem>>.Success(normalized);
    }

    private static bool TryResolveTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        timeZone = null!;
        return false;
    }

    private static bool TryConvertToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone, out DateTimeOffset utc)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }
        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            utc = new DateTimeOffset(local, offset).ToUniversalTime();
            return true;
        }
        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        return true;
    }

    private static int ToIsoDayOfWeek(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static string CreateOccurrenceId(Guid scheduleId, DateOnly date)
    {
        var canonical = $"{scheduleId:N}|{date:yyyy-MM-dd}";
        return $"recurring-lesson-v1:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow().ToUniversalTime();
    private static bool TryGetUserId(string? value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;
    private static RecurringLessonResult<T> Failure<T>(RecurringLessonErrorCode code) => RecurringLessonResult<T>.Failure(code);

    private sealed record NormalizedItem(string ExternalId, string Title, int IsoDayOfWeek,
        TimeOnly StartLocalTime, TimeOnly EndLocalTime, bool NotificationEnabled,
        int ReminderMinutesBefore, bool LockRequired);
    private sealed record ScheduleProjection(Guid Id, string Title, int IsoDayOfWeek,
        TimeOnly StartLocalTime, TimeOnly EndLocalTime, string TimeZoneId, string Source,
        int ReminderMinutesBefore, bool LockRequired, DateTimeOffset LastSeenAtUtc);
}
