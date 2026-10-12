namespace LgsTakip.Api.Application.ParentLinking.Contracts;

public sealed class LinkedParentResponse
{
    public Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
}
