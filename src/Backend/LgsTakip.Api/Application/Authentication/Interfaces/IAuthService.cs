using LgsTakip.Api.Application.Authentication.Contracts;
using LgsTakip.Api.Application.Authentication.Results;

namespace LgsTakip.Api.Application.Authentication.Interfaces;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}
