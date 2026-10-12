using LgsTakip.Api.Application.Authentication.Contracts;

namespace LgsTakip.Api.Application.Authentication.Results;

public sealed class AuthResult
{
    private AuthResult(
        bool succeeded,
        AuthResponse? response,
        AuthErrorCode errorCode)
    {
        Succeeded = succeeded;
        Response = response;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }
    public AuthResponse? Response { get; }
    public AuthErrorCode ErrorCode { get; }

    public static AuthResult Success(AuthResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new AuthResult(true, response, AuthErrorCode.None);
    }

    public static AuthResult Failure(AuthErrorCode errorCode)
    {
        if (errorCode is AuthErrorCode.None)
        {
            throw new ArgumentException(
                "A failure result must include an authentication error code.",
                nameof(errorCode));
        }

        return new AuthResult(false, null, errorCode);
    }
}
