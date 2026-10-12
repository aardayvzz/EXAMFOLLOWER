using Microsoft.AspNetCore.Authentication;

namespace LgsTakip.Api.Infrastructure.CollectorAuthentication;

public sealed class CollectorAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SectionName = "CollectorAuthentication";

    public string ApiKeyHeaderName { get; set; } = "X-Collector-Api-Key";
    public string ApiKey { get; set; } = string.Empty;
}
