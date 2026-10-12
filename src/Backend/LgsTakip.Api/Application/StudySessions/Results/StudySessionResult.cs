namespace LgsTakip.Api.Application.StudySessions.Results;

public sealed class StudySessionResult<T>
{
    private StudySessionResult(
        bool succeeded,
        T? value,
        StudySessionErrorCode errorCode)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public StudySessionErrorCode ErrorCode { get; }

    public static StudySessionResult<T> Success(T? value)
    {
        return new StudySessionResult<T>(
            true,
            value,
            StudySessionErrorCode.None);
    }

    public static StudySessionResult<T> Failure(
        StudySessionErrorCode errorCode)
    {
        if (errorCode is StudySessionErrorCode.None)
        {
            throw new ArgumentException(
                "A failure result must include a study session error code.",
                nameof(errorCode));
        }

        return new StudySessionResult<T>(false, default, errorCode);
    }
}
