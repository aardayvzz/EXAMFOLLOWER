namespace LgsTakip.Api.Application.RecurringLessons.Results;

public sealed class RecurringLessonResult<T>
{
    private RecurringLessonResult(bool succeeded, T? value, RecurringLessonErrorCode errorCode)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public RecurringLessonErrorCode ErrorCode { get; }

    public static RecurringLessonResult<T> Success(T value) => new(true, value, RecurringLessonErrorCode.None);
    public static RecurringLessonResult<T> Failure(RecurringLessonErrorCode errorCode) => new(false, default, errorCode);
}
