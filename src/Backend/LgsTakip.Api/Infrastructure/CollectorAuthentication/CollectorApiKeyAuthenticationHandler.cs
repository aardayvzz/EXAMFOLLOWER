using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LgsTakip.Api.Infrastructure.CollectorAuthentication;

public sealed class CollectorApiKeyAuthenticationHandler(
    IOptionsMonitor<CollectorAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<CollectorAuthenticationOptions>(
        options,
        logger,
        encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(
                Options.ApiKeyHeaderName,
                out var suppliedValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (suppliedValues.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Collector authentication failed."));
        }

        var suppliedKey = suppliedValues[0];
        if (string.IsNullOrEmpty(suppliedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Collector authentication failed."));
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        var expectedBytes = Encoding.UTF8.GetBytes(Options.ApiKey);

        if (!CryptographicOperations.FixedTimeEquals(
                suppliedBytes,
                expectedBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Collector authentication failed."));
        }

        var claims = new[]
        {
            new Claim(
                CollectorAuthenticationDefaults.ClaimType,
                CollectorAuthenticationDefaults.ClaimValue)
        };
        var identity = new ClaimsIdentity(
            claims,
            CollectorAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(
            principal,
            CollectorAuthenticationDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
