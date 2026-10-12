using System.ComponentModel.DataAnnotations;

namespace LgsTakip.Api.Application.Authentication.Contracts;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}
