using LgsTakip.Api.Domain.Enums;

namespace LgsTakip.Api.Application.Authentication.Contracts;

public sealed class AuthenticatedUserResponse
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public UserRole Role { get; init; }
}
