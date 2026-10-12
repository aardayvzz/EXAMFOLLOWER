using LgsTakip.Api.Application.Authentication.Contracts;
using LgsTakip.Api.Application.Authentication.Interfaces;
using LgsTakip.Api.Application.Authentication.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("Authentication")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);

        if (result.Succeeded && result.Response is not null)
        {
            return StatusCode(StatusCodes.Status201Created, result.Response);
        }

        return ToProblemDetails(result.ErrorCode);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);

        if (result.Succeeded && result.Response is not null)
        {
            return Ok(result.Response);
        }

        return ToProblemDetails(result.ErrorCode);
    }

    private ObjectResult ToProblemDetails(AuthErrorCode errorCode)
    {
        var (status, title, detail) = errorCode switch
        {
            AuthErrorCode.InvalidInput => (
                StatusCodes.Status400BadRequest,
                "Invalid authentication request",
                "Gönderilen kimlik doğrulama bilgileri geçersiz."),
            AuthErrorCode.InvalidRole => (
                StatusCodes.Status400BadRequest,
                "Invalid user role",
                "Rol Student veya Parent olmalıdır."),
            AuthErrorCode.EmailAlreadyExists => (
                StatusCodes.Status409Conflict,
                "Email already registered",
                "Bu e-posta adresiyle kayıtlı bir kullanıcı zaten var."),
            AuthErrorCode.InvalidCredentials => (
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                "E-posta veya parola geçersiz."),
            AuthErrorCode.InactiveUser => (
                StatusCodes.Status403Forbidden,
                "User account is inactive",
                "Kullanıcı hesabı etkin değil."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Authentication service error",
                "Kimlik doğrulama işlemi tamamlanamadı.")
        };

        return Problem(
            type: $"https://httpstatuses.com/{status}",
            title: title,
            statusCode: status,
            detail: detail,
            instance: HttpContext.Request.Path);
    }
}
