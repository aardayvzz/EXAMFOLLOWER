using System.ComponentModel.DataAnnotations;
using LgsTakip.Api.Domain.Enums;

namespace LgsTakip.Api.Application.Authentication.Contracts;

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public required string Email { get; init; }

    [Required]
    [MinLength(10)]
    public required string Password { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public required string DisplayName { get; init; }

    [EnumDataType(typeof(UserRole))]
    public required UserRole Role { get; init; }
}
