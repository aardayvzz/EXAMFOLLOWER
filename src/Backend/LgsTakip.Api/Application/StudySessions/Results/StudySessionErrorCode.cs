namespace LgsTakip.Api.Application.StudySessions.Results;

public enum StudySessionErrorCode
{
    None,
    InvalidInput,
    InvalidUserIdentity,
    NotFound,
    ActiveSessionAlreadyExists,
    InvalidStateTransition,
    DataIntegrityViolation
}
