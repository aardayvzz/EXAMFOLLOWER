namespace LgsTakip.Api.Application.Authentication.Contracts;

public sealed class AuthResponse
{
    public required string AccessToken { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public required AuthenticatedUserResponse User { get; init; }
}
