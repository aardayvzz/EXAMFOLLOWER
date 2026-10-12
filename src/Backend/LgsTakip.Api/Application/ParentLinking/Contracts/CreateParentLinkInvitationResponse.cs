namespace LgsTakip.Api.Application.ParentLinking.Contracts;

public sealed class CreateParentLinkInvitationResponse
{
    public Guid InvitationId { get; init; }
    public required string LinkToken { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
