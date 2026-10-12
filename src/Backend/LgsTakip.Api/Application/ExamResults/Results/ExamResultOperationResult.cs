namespace LgsTakip.Api.Application.ExamResults.Results;

public sealed class ExamResultOperationResult<T>
{
    private ExamResultOperationResult(
        bool succeeded,
        T? value,
        ExamResultErrorCode errorCode)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public ExamResultErrorCode ErrorCode { get; }

    public static ExamResultOperationResult<T> Success(T? value) =>
        new(true, value, ExamResultErrorCode.None);

    public static ExamResultOperationResult<T> Failure(
        ExamResultErrorCode errorCode)
    {
        if (errorCode is ExamResultErrorCode.None)
        {
            throw new ArgumentException(
                "A failure result must include an exam result error code.",
                nameof(errorCode));
        }

        return new(false, default, errorCode);
    }
}
