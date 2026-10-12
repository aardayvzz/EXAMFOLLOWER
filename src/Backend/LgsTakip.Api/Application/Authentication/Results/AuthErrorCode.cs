namespace LgsTakip.Api.Application.Authentication.Results;

public enum AuthErrorCode
{
    None,
    InvalidInput,
    EmailAlreadyExists,
    InvalidCredentials,
    InactiveUser,
    InvalidRole
}
