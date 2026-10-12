namespace LgsTakip.Api.Application.ExamResults.Results;

public enum ExamResultErrorCode
{
    None,
    InvalidInput,
    InvalidUserIdentity,
    NotFound,
    StudentNotFound,
    InvalidDateRange,
    DataIntegrityViolation
}
