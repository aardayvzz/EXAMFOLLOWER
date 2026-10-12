namespace LgsTakip.Api.Application.ZoomLessons.Results;

public sealed class ZoomLessonOperationResult<T>
{
    private ZoomLessonOperationResult(
        bool succeeded,
        T? value,
        ZoomLessonErrorCode errorCode)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public ZoomLessonErrorCode ErrorCode { get; }

    public static ZoomLessonOperationResult<T> Success(T? value) =>
        new(true, value, ZoomLessonErrorCode.None);

    public static ZoomLessonOperationResult<T> Failure(
        ZoomLessonErrorCode errorCode)
    {
        if (errorCode is ZoomLessonErrorCode.None)
        {
            throw new ArgumentException(
                "A failure result must include a Zoom lesson error code.",
                nameof(errorCode));
        }

        return new(false, default, errorCode);
    }
}
