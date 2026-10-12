namespace LgsTakip.Api.Application.ParentAnalytics.Results;

public sealed class ParentAnalyticsResult<T>
{
    private ParentAnalyticsResult(
        bool succeeded,
        T? value,
        ParentAnalyticsErrorCode errorCode)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public ParentAnalyticsErrorCode ErrorCode { get; }

    public static ParentAnalyticsResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ParentAnalyticsResult<T>(
            true,
            value,
            ParentAnalyticsErrorCode.None);
    }

    public static ParentAnalyticsResult<T> Failure(
        ParentAnalyticsErrorCode errorCode)
    {
        if (errorCode is ParentAnalyticsErrorCode.None)
        {
            throw new ArgumentException(
                "A failure result must include a parent analytics error code.",
                nameof(errorCode));
        }

        return new ParentAnalyticsResult<T>(false, default, errorCode);
    }
}
