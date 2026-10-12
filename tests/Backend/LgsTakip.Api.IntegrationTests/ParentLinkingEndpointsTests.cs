using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgsTakip.Api.IntegrationTests;

public sealed class ParentLinkingEndpointsTests
{
    private const string ValidPassword = "SecurePass123";
    private const string GenericInvitationDetail =
        "Davet token'ı geçersiz veya artık kullanılamıyor.";
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateInvitation_StudentCreatesCryptographicToken()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await client.PostAsync(InvitationPath, null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invitation = await ReadInvitationAsync(response);
        Assert.NotEqual(Guid.Empty, invitation.Id);
        Assert.Equal(43, invitation.Token.Length);
        Assert.DoesNotContain('=', invitation.Token);
        Assert.True(invitation.ExpiresAtUtc > InitialUtc);
    }

    [Fact]
    public async Task CreateInvitation_WithParentToken_ReturnsForbidden()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, parent.AccessToken);

        using var response = await client.PostAsync(InvitationPath, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvitation_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(InvitationPath, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvitation_DoesNotStoreRawToken()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var response = await client.PostAsync(InvitationPath, null);
        var invitation = await ReadInvitationAsync(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var stored = await dbContext.ParentStudentInvitations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invitation.Id);

        Assert.False(
            Encoding.UTF8.GetBytes(invitation.Token)
                .SequenceEqual(stored.TokenHash));
        Assert.DoesNotContain(
            invitation.Token,
            Convert.ToHexString(stored.TokenHash),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateInvitation_StoresThirtyTwoByteHash()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var response = await client.PostAsync(InvitationPath, null);
        var invitation = await ReadInvitationAsync(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var tokenHash = await dbContext.ParentStudentInvitations
            .AsNoTracking()
            .Where(candidate => candidate.Id == invitation.Id)
            .Select(candidate => candidate.TokenHash)
            .SingleAsync();

        Assert.Equal(32, tokenHash.Length);
    }

    [Fact]
    public async Task CreateInvitation_SecondInvitationRevokesFirst()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var firstResponse = await client.PostAsync(InvitationPath, null);
        var first = await ReadInvitationAsync(firstResponse);
        using var secondResponse = await client.PostAsync(InvitationPath, null);
        var second = await ReadInvitationAsync(secondResponse);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var invitations = await dbContext.ParentStudentInvitations
            .AsNoTracking()
            .Where(candidate => candidate.StudentId == student.Id)
            .ToDictionaryAsync(candidate => candidate.Id);

        Assert.NotNull(invitations[first.Id].RevokedAtUtc);
        Assert.Null(invitations[second.Id].RevokedAtUtc);
    }

    [Fact]
    public async Task Redeem_RevokedTokenReturnsGenericError()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, student.AccessToken);
        using var firstResponse = await client.PostAsync(InvitationPath, null);
        var first = await ReadInvitationAsync(firstResponse);
        using var secondResponse = await client.PostAsync(InvitationPath, null);
        Authorize(client, parent.AccessToken);

        using var response = await RedeemAsync(client, first.Token);

        await AssertGenericInvalidInvitationAsync(response);
    }

    [Fact]
    public async Task Redeem_ExpiredTokenReturnsGenericError()
    {
        var timeProvider = new AdjustableTimeProvider(InitialUtc);
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, student.AccessToken);
        using var createResponse = await client.PostAsync(InvitationPath, null);
        var invitation = await ReadInvitationAsync(createResponse);
        timeProvider.Advance(TimeSpan.FromMinutes(16));
        Authorize(client, parent.AccessToken);

        using var response = await RedeemAsync(client, invitation.Token);

        await AssertGenericInvalidInvitationAsync(response);
    }

    [Fact]
    public async Task Redeem_InvalidTokenReturnsGenericError()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, parent.AccessToken);

        using var response = await RedeemAsync(
            client,
            CreateUnstoredToken());

        await AssertGenericInvalidInvitationAsync(response);
    }

    [Fact]
    public async Task Redeem_ValidTokenReturnsLinkedStudent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (student, parent, invitation) = await CreateUsersAndInvitationAsync(
            client);
        Authorize(client, parent.AccessToken);

        using var response = await RedeemAsync(client, invitation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.Equal(student.Id, payload.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Redeem_CreatesParentStudentRelationship()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (student, parent, invitation) = await CreateUsersAndInvitationAsync(
            client);
        Authorize(client, parent.AccessToken);
        using var response = await RedeemAsync(client, invitation.Token);

        Assert.True(await RelationshipExistsAsync(
            factory,
            parent.Id,
            student.Id));
    }

    [Fact]
    public async Task Redeem_MarksInvitationConsumedByParent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (_, parent, invitation) = await CreateUsersAndInvitationAsync(client);
        Authorize(client, parent.AccessToken);
        using var response = await RedeemAsync(client, invitation.Token);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var stored = await dbContext.ParentStudentInvitations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invitation.Id);
        Assert.NotNull(stored.ConsumedAtUtc);
        Assert.Equal(parent.Id, stored.ConsumedByParentId);
    }

    [Fact]
    public async Task Redeem_ResponseDoesNotExposeTokenOrHash()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (_, parent, invitation) = await CreateUsersAndInvitationAsync(client);
        Authorize(client, parent.AccessToken);

        using var response = await RedeemAsync(client, invitation.Token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("linkToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invitation.Token, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redeem_SameParentRepeatingTokenIsIdempotent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (_, parent, invitation) = await CreateUsersAndInvitationAsync(client);
        Authorize(client, parent.AccessToken);
        using var firstResponse = await RedeemAsync(client, invitation.Token);

        using var secondResponse = await RedeemAsync(client, invitation.Token);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Redeem_SecondParentCannotUseConsumedToken()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (_, firstParent, invitation) = await CreateUsersAndInvitationAsync(
            client);
        var secondParent = await RegisterAsync(client, "Parent");
        Authorize(client, firstParent.AccessToken);
        using var firstResponse = await RedeemAsync(client, invitation.Token);
        Authorize(client, secondParent.AccessToken);

        using var secondResponse = await RedeemAsync(client, invitation.Token);

        await AssertGenericInvalidInvitationAsync(secondResponse);
    }

    [Fact]
    public async Task Redeem_ConcurrentParentsOnlyOneSucceeds()
    {
        await using var factory = await CreateFactoryAsync();
        using var setupClient = CreateClient(factory);
        var student = await RegisterAsync(setupClient, "Student");
        var firstParent = await RegisterAsync(setupClient, "Parent");
        var secondParent = await RegisterAsync(setupClient, "Parent");
        Authorize(setupClient, student.AccessToken);
        using var createResponse = await setupClient.PostAsync(InvitationPath, null);
        var invitation = await ReadInvitationAsync(createResponse);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        Authorize(firstClient, firstParent.AccessToken);
        Authorize(secondClient, secondParent.AccessToken);

        var responses = await Task.WhenAll(
            RedeemAsync(firstClient, invitation.Token),
            RedeemAsync(secondClient, invitation.Token));

        try
        {
            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.BadRequest));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Redeem_DifferentInvitationsLinkTwoParentsToStudent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var firstParent = await RegisterAsync(client, "Parent");
        var secondParent = await RegisterAsync(client, "Parent");
        Authorize(client, student.AccessToken);
        using var firstCreate = await client.PostAsync(InvitationPath, null);
        var firstInvitation = await ReadInvitationAsync(firstCreate);
        Authorize(client, firstParent.AccessToken);
        using var firstRedeem = await RedeemAsync(client, firstInvitation.Token);
        Authorize(client, student.AccessToken);
        using var secondCreate = await client.PostAsync(InvitationPath, null);
        var secondInvitation = await ReadInvitationAsync(secondCreate);
        Authorize(client, secondParent.AccessToken);
        using var secondRedeem = await RedeemAsync(client, secondInvitation.Token);

        Assert.True(await RelationshipExistsAsync(factory, firstParent.Id, student.Id));
        Assert.True(await RelationshipExistsAsync(factory, secondParent.Id, student.Id));
    }

    [Fact]
    public async Task Redeem_LinkedStudentAppearsInParentAnalytics()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (student, parent, invitation) = await CreateUsersAndInvitationAsync(
            client);
        Authorize(client, parent.AccessToken);
        using var redeemResponse = await RedeemAsync(client, invitation.Token);

        using var response = await client.GetAsync("/api/parent/students");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(student.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLinkedParents_ReturnsParentsForStudent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (student, parent, invitation) = await CreateUsersAndInvitationAsync(
            client);
        Authorize(client, parent.AccessToken);
        using var redeemResponse = await RedeemAsync(client, invitation.Token);
        Authorize(client, student.AccessToken);

        using var response = await client.GetAsync("/api/student/parent-links");

        using var payload = await ReadJsonAsync(response);
        var linkedParent = Assert.Single(payload.RootElement.EnumerateArray());
        Assert.Equal(parent.Id, linkedParent.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task RemoveStudentLink_ParentRemovesOwnRelationship()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var parent = await RegisterAsync(client, "Parent");
        await LinkAsync(factory, parent.Id, student.Id);
        Authorize(client, parent.AccessToken);

        using var response = await client.DeleteAsync(
            $"/api/parent/student-links/{student.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await RelationshipExistsAsync(factory, parent.Id, student.Id));
    }

    [Fact]
    public async Task RemoveParentLink_StudentRemovesOwnRelationship()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var parent = await RegisterAsync(client, "Parent");
        await LinkAsync(factory, parent.Id, student.Id);
        Authorize(client, student.AccessToken);

        using var response = await client.DeleteAsync(
            $"/api/student/parent-links/{parent.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await RelationshipExistsAsync(factory, parent.Id, student.Id));
    }

    [Fact]
    public async Task RemoveLink_CannotRemoveAnotherUsersRelationship()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var firstStudent = await RegisterAsync(client, "Student");
        var secondStudent = await RegisterAsync(client, "Student");
        var firstParent = await RegisterAsync(client, "Parent");
        var secondParent = await RegisterAsync(client, "Parent");
        await LinkAsync(factory, firstParent.Id, firstStudent.Id);
        await LinkAsync(factory, secondParent.Id, secondStudent.Id);
        Authorize(client, firstParent.AccessToken);
        using var parentResponse = await client.DeleteAsync(
            $"/api/parent/student-links/{secondStudent.Id}");
        Authorize(client, firstStudent.AccessToken);
        using var studentResponse = await client.DeleteAsync(
            $"/api/student/parent-links/{secondParent.Id}");

        Assert.True(await RelationshipExistsAsync(
            factory,
            secondParent.Id,
            secondStudent.Id));
        Assert.True(await RelationshipExistsAsync(
            factory,
            firstParent.Id,
            firstStudent.Id));
    }

    [Fact]
    public async Task RevokeInvitation_NonOwnerStudentReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var owner = await RegisterAsync(client, "Student");
        var otherStudent = await RegisterAsync(client, "Student");
        Authorize(client, owner.AccessToken);
        using var createResponse = await client.PostAsync(InvitationPath, null);
        var invitation = await ReadInvitationAsync(createResponse);
        Authorize(client, otherStudent.AccessToken);

        using var response = await client.DeleteAsync(
            $"{InvitationPath}/{invitation.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokeInvitation_ConsumedInvitationReturnsConflict()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (student, parent, invitation) = await CreateUsersAndInvitationAsync(
            client);
        Authorize(client, parent.AccessToken);
        using var redeemResponse = await RedeemAsync(client, invitation.Token);
        Authorize(client, student.AccessToken);

        using var response = await client.DeleteAsync(
            $"{InvitationPath}/{invitation.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvitation_ExceedingRateLimitReturnsTooManyRequests()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        for (var requestNumber = 1; requestNumber <= 5; requestNumber++)
        {
            using var allowed = await client.PostAsync(InvitationPath, null);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var rejected = await client.PostAsync(InvitationPath, null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Redeem_ExceedingRateLimitReturnsTooManyRequests()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, parent.AccessToken);
        var unstoredToken = CreateUnstoredToken();

        for (var requestNumber = 1; requestNumber <= 5; requestNumber++)
        {
            using var allowed = await RedeemAsync(
                client,
                unstoredToken);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var rejected = await RedeemAsync(
            client,
            unstoredToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Redeem_ResponseAndLogsDoNotLeakTokenOrHash()
    {
        var logs = new ConcurrentQueue<string>();
        await using var factory = await CreateFactoryAsync(logMessages: logs);
        using var client = CreateClient(factory);
        var (_, parent, invitation) = await CreateUsersAndInvitationAsync(client);
        Authorize(client, parent.AccessToken);

        using var response = await RedeemAsync(client, invitation.Token);
        var responseBody = await response.Content.ReadAsStringAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var hash = await dbContext.ParentStudentInvitations
            .AsNoTracking()
            .Where(candidate => candidate.Id == invitation.Id)
            .Select(candidate => candidate.TokenHash)
            .SingleAsync();
        var hashText = Convert.ToHexString(hash);
        var combinedLogs = string.Join(Environment.NewLine, logs);

        Assert.DoesNotContain(invitation.Token, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(hashText, responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invitation.Token, combinedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(hashText, combinedLogs, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(
        RegisteredUser Student,
        RegisteredUser Parent,
        InvitationData Invitation)> CreateUsersAndInvitationAsync(
            HttpClient client)
    {
        var student = await RegisterAsync(client, "Student");
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, student.AccessToken);
        using var createResponse = await client.PostAsync(InvitationPath, null);
        var invitation = await ReadInvitationAsync(createResponse);
        return (student, parent, invitation);
    }

    private static async Task<RegisteredUser> RegisterAsync(
        HttpClient client,
        string role)
    {
        var email = $"parent-link-{Guid.NewGuid():N}@example.com";
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email,
                password = ValidPassword,
                displayName = $"{role} User",
                role
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        return new RegisteredUser(
            payload.RootElement.GetProperty("user").GetProperty("id").GetGuid(),
            payload.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static async Task LinkAsync(
        SqlServerWebApplicationFactory factory,
        Guid parentId,
        Guid studentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var parent = await dbContext.Users.SingleAsync(user => user.Id == parentId);
        var student = await dbContext.Users.SingleAsync(user => user.Id == studentId);
        parent.Students.Add(student);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<bool> RelationshipExistsAsync(
        SqlServerWebApplicationFactory factory,
        Guid parentId,
        Guid studentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        return await dbContext.Users
            .AsNoTracking()
            .AnyAsync(parent => parent.Id == parentId
                && parent.Students.Any(student => student.Id == studentId));
    }

    private static Task<HttpResponseMessage> RedeemAsync(
        HttpClient client,
        string token)
    {
        return client.PostAsJsonAsync(
            "/api/parent/student-links/redeem",
            new { linkToken = token });
    }

    private static async Task<InvitationData> ReadInvitationAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        return new InvitationData(
            payload.RootElement.GetProperty("invitationId").GetGuid(),
            payload.RootElement.GetProperty("linkToken").GetString()!,
            payload.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
    }

    private static async Task AssertGenericInvalidInvitationAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(GenericInvitationDetail, problem.Detail);
    }

    private static async Task<SqlServerWebApplicationFactory> CreateFactoryAsync(
        TimeProvider? timeProvider = null,
        ConcurrentQueue<string>? logMessages = null)
    {
        var factory = new SqlServerWebApplicationFactory(
            timeProvider ?? new AdjustableTimeProvider(InitialUtc),
            logMessages);

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

    private static void Authorize(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string CreateUnstoredToken()
    {
        return WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
    }

    private const string InvitationPath =
        "/api/student/parent-links/invitations";

    private sealed record RegisteredUser(Guid Id, string AccessToken);

    private sealed record InvitationData(
        Guid Id,
        string Token,
        DateTimeOffset ExpiresAtUtc);
}
