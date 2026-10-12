using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LgsTakip.Api.IntegrationTests;

public sealed class AuthenticationRateLimitingTests
{
    [Fact]
    public async Task Login_ExceedingAuthenticationPolicyLimit_ReturnsTooManyRequests()
    {
        await using var factory = new SqlServerWebApplicationFactory();
        await factory.InitializeDatabaseAsync();

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var allowedResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email = $"rate-{requestNumber}@example.com",
                    password = "SecurePass123"
                });

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                allowedResponse.StatusCode);
        }

        using var rejectedResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = "rate-rejected@example.com",
                password = "SecurePass123"
            });

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
    }
}
