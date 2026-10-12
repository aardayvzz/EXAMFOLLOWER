using System.ComponentModel.DataAnnotations;

namespace LgsTakip.Api.Application.ParentLinking.Contracts;

public sealed class RedeemParentLinkRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string LinkToken { get; init; } = string.Empty;
}
