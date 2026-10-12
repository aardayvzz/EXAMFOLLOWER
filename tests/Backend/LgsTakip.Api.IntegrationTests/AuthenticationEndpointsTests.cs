using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgsTakip.Api.IntegrationTests;

public sealed class AuthenticationEndpointsTests
{
    private const string ValidPassword = "SecurePass123";
    private const string InvalidCredentialsDetail = "E-posta veya parola geçersiz.";

    [Fact]
    public async Task Register_ValidStudent_CreatesUserAndReturnsToken()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var email = UniqueEmail("student");

        using var response = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "Öğrenci Adı",
            "Student");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var payload = await ReadJsonAsync(response);
        var root = payload.RootElement;
        Assert.Equal("Bearer", root.GetProperty("tokenType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("accessToken").GetString()));
        Assert.Equal(email, root.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Öğrenci Adı", root.GetProperty("user").GetProperty("displayName").GetString());
        Assert.Equal("Student", root.GetProperty("user").GetProperty("role").GetString());

        var responseBody = root.GetRawText();
        Assert.DoesNotContain("passwordHash", responseBody, StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var user = await dbContext.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == email.ToUpperInvariant());

        Assert.NotEqual(ValidPassword, user.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
    }

    [Fact]
    public async Task Register_ValidParent_ReturnsParentRole()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await RegisterAsync(
            client,
            UniqueEmail("parent"),
            ValidPassword,
            "Veli Adı",
            "Parent");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var payload = await ReadJsonAsync(response);
        Assert.Equal(
            "Parent",
            payload.RootElement.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflictAndDoesNotCreateSecondUser()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var email = UniqueEmail("duplicate");

        using var firstResponse = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "İlk Kullanıcı",
            "Student");
        using var secondResponse = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "İkinci Kullanıcı",
            "Student");

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var problem = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var userCount = await dbContext.Users.CountAsync(
            candidate => candidate.NormalizedEmail == email.ToUpperInvariant());
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task Register_DuplicateEmailWithDifferentCasing_ReturnsConflict()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var localPart = $"case-{Guid.NewGuid():N}";
        var lowerCaseEmail = $"{localPart}@example.com";
        var upperCaseEmail = lowerCaseEmail.ToUpperInvariant();

        using var firstResponse = await RegisterAsync(
            client,
            lowerCaseEmail,
            ValidPassword,
            "İlk Kullanıcı",
            "Student");
        using var secondResponse = await RegisterAsync(
            client,
            upperCaseEmail,
            ValidPassword,
            "İkinci Kullanıcı",
            "Student");

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await RegisterAsync(
            client,
            "not-an-email",
            ValidPassword,
            "Öğrenci Adı",
            "Student");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_PasswordWithoutDigit_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await RegisterAsync(
            client,
            UniqueEmail("password-rule"),
            "OnlyLetters",
            "Öğrenci Adı",
            "Student");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_UndefinedNumericRole_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = UniqueEmail("invalid-role"),
                password = ValidPassword,
                displayName = "Öğrenci Adı",
                role = 99
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenAndUser()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var email = UniqueEmail("login");

        using var registerResponse = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "Giriş Öğrencisi",
            "Student");
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        using var loginResponse = await LoginAsync(client, email, ValidPassword);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var payload = await ReadJsonAsync(loginResponse);
        Assert.Equal("Bearer", payload.RootElement.GetProperty("tokenType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            payload.RootElement.GetProperty("accessToken").GetString()));
        Assert.Equal(
            email,
            payload.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal(
            "Giriş Öğrencisi",
            payload.RootElement.GetProperty("user").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Login_EmailWithDifferentCasing_Succeeds()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var email = UniqueEmail("login-case");

        using var registerResponse = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "Öğrenci Adı",
            "Student");
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        using var loginResponse = await LoginAsync(
            client,
            email.ToUpperInvariant(),
            ValidPassword);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsGenericUnauthorizedProblem()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var email = UniqueEmail("wrong-password");

        using var registerResponse = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "Öğrenci Adı",
            "Student");
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        using var loginResponse = await LoginAsync(
            client,
            email,
            "IncorrectPass123");

        await AssertInvalidCredentialsProblemAsync(loginResponse);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsSameGenericUnauthorizedProblem()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await LoginAsync(
            client,
            UniqueEmail("unknown"),
            ValidPassword);

        await AssertInvalidCredentialsProblemAsync(response);
    }

    [Fact]
    public async Task Login_InactiveUser_ReturnsForbidden()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var email = UniqueEmail("inactive");

        using var registerResponse = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "Pasif Öğrenci",
            "Student");
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
            var user = await dbContext.Users.SingleAsync(
                candidate => candidate.NormalizedEmail == email.ToUpperInvariant());
            user.IsActive = false;
            await dbContext.SaveChangesAsync();
        }

        using var loginResponse = await LoginAsync(client, email, ValidPassword);

        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
        var problem = await loginResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    [Fact]
    public async Task Register_IssuedJwt_IsCryptographicallyValidAndContainsSafeClaims()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var email = UniqueEmail("jwt");

        using var response = await RegisterAsync(
            client,
            email,
            ValidPassword,
            "Token Öğrencisi",
            "Student");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var payload = await ReadJsonAsync(response);
        var accessToken = payload.RootElement.GetProperty("accessToken").GetString();
        var userId = payload.RootElement.GetProperty("user").GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var user = await dbContext.Users.SingleAsync(
            candidate => candidate.Id == userId);

        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        var principal = handler.ValidateToken(
            accessToken,
            factory.CreateTokenValidationParameters(),
            out var validatedToken);
        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);

        Assert.Equal(SqlServerWebApplicationFactory.ExpectedIssuer, jwt.Issuer);
        Assert.Contains(
            SqlServerWebApplicationFactory.ExpectedAudience,
            jwt.Audiences);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddMinutes(61));
        Assert.Equal(userId.ToString(), principal.FindFirst("sub")?.Value);
        Assert.Equal(email, principal.FindFirst("email")?.Value);
        Assert.Equal("Student", principal.FindFirst("role")?.Value);
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirst("jti")?.Value));
        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Type.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Value == ValidPassword || claim.Value == user.PasswordHash);
    }

    private static async Task<SqlServerWebApplicationFactory> CreateFactoryAsync()
    {
        var factory = new SqlServerWebApplicationFactory();

        try
        {
            await factory.InitializeDatabaseAsync();
            return factory;
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    private static HttpClient CreateClient(SqlServerWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string email,
        string password,
        string displayName,
        string role)
    {
        return client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password, displayName, role });
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        return client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password });
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task AssertInvalidCredentialsProblemAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        Assert.Equal(InvalidCredentialsDetail, problem.Detail);
        Assert.DoesNotContain("passwordHash", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniqueEmail(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}@example.com";
    }
}
