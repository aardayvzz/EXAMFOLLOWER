namespace LgsTakip.Api.Infrastructure.ParentLinking;

public sealed class ParentLinkingOptions
{
    public const string SectionName = "ParentLinking";

    public int InvitationLifetimeMinutes { get; init; }
}
