namespace LgsTakip.Api.Application.RecurringLessons.Results;

public enum RecurringLessonErrorCode
{
    None,
    InvalidInput,
    InvalidUserIdentity,
    StudentNotFound,
    UnsupportedSource,
    InvalidTimeZone,
    DataIntegrityViolation
}
