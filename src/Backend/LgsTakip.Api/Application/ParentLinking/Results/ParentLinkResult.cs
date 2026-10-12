namespace LgsTakip.Api.Application.ParentLinking.Results;

public sealed class ParentLinkResult<T>
{
    private ParentLinkResult(
        bool succeeded,
        T? value,
        ParentLinkErrorCode errorCode)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public ParentLinkErrorCode ErrorCode { get; }

    public static ParentLinkResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ParentLinkResult<T>(true, value, ParentLinkErrorCode.None);
    }

    public static ParentLinkResult<T> Failure(ParentLinkErrorCode errorCode)
    {
        if (errorCode is ParentLinkErrorCode.None)
        {
            throw new ArgumentException(
                "A failure result must include a parent link error code.",
                nameof(errorCode));
        }

        return new ParentLinkResult<T>(false, default, errorCode);
    }
}
