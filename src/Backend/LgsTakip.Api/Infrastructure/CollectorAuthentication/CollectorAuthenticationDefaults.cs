namespace LgsTakip.Api.Infrastructure.CollectorAuthentication;

public static class CollectorAuthenticationDefaults
{
    public const string Scheme = "CollectorApiKey";
    public const string Policy = "CollectorOnly";
    public const string ClaimType = "collector_identity";
    public const string ClaimValue = "verified";
}
