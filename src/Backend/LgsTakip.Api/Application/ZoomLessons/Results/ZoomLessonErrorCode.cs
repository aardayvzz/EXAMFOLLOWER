namespace LgsTakip.Api.Application.ZoomLessons.Results;

public enum ZoomLessonErrorCode
{
    None,
    InvalidInput,
    InvalidUserIdentity,
    StudentNotFound,
    LessonNotFound,
    InvalidDateRange,
    InvalidZoomUrl,
    UnsupportedSource,
    DataConflict,
    DataIntegrityViolation
}
