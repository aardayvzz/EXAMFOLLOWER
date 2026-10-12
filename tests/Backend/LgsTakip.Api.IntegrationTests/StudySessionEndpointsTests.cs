using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgsTakip.Api.IntegrationTests;

public sealed class StudySessionEndpointsTests
{
    private const string ValidPassword = "SecurePass123";
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await StartAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_WithParentToken_ReturnsForbidden()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, parent.AccessToken);

        using var response = await StartAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Start_WithStudentToken_ReturnsCreated()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await StartAsync(client, 25);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        using var payload = await ReadJsonAsync(response);
        var root = payload.RootElement;
        Assert.Equal("Running", root.GetProperty("status").GetString());
        Assert.Equal(InitialUtc, root.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("endedAtUtc").ValueKind);
        Assert.Equal(25, root.GetProperty("plannedDurationMinutes").GetInt32());
        Assert.Equal(0, root.GetProperty("completedDurationSeconds").GetInt32());
    }

    [Fact]
    public async Task Start_ResponseDoesNotExposeRoleOrOtherUserData()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await StartAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("studentId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("displayName", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_WhenStudentAlreadyHasRunningSession_ReturnsConflict()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var firstResponse = await StartAsync(client);
        using var secondResponse = await StartAsync(client);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Start_ForDifferentStudents_CreatesSeparateSessions()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var firstStudent = await RegisterAsync(client, "Student");
        var secondStudent = await RegisterAsync(client, "Student");

        Authorize(client, firstStudent.AccessToken);
        using var firstResponse = await StartAsync(client);
        Authorize(client, secondStudent.AccessToken);
        using var secondResponse = await StartAsync(client);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotEqual(
            await ReadSessionIdAsync(firstResponse),
            await ReadSessionIdAsync(secondResponse));
    }

    [Fact]
    public async Task GetActive_WithRunningSession_ReturnsOk()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);

        using var response = await client.GetAsync("/api/study-sessions/active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.Equal(sessionId, payload.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetActive_WithoutRunningSession_ReturnsNoContent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await client.GetAsync("/api/study-sessions/active");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForOwner_ReturnsOk()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);

        using var response = await client.GetAsync($"/api/study-sessions/{sessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForDifferentStudent_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var owner = await RegisterAsync(client, "Student");
        var otherStudent = await RegisterAsync(client, "Student");
        Authorize(client, owner.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        Authorize(client, otherStudent.AccessToken);

        using var response = await client.GetAsync($"/api/study-sessions/{sessionId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Complete_RunningSession_ReturnsCompletedWithElapsedSeconds()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        timeProvider.Advance(TimeSpan.FromSeconds(65));

        using var response = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/complete",
            null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.Equal("Completed", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal(65, payload.RootElement.GetProperty("completedDurationSeconds").GetInt32());
        Assert.Equal(
            InitialUtc.AddSeconds(65),
            payload.RootElement.GetProperty("endedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task Complete_CompletedSession_IsIdempotent()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        using var firstResponse = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/complete",
            null);
        using var firstPayload = await ReadJsonAsync(firstResponse);
        var firstEndedAt = firstPayload.RootElement
            .GetProperty("endedAtUtc")
            .GetDateTimeOffset();
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        using var secondResponse = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/complete",
            null);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var secondPayload = await ReadJsonAsync(secondResponse);
        Assert.Equal(
            firstEndedAt,
            secondPayload.RootElement.GetProperty("endedAtUtc").GetDateTimeOffset());
        Assert.Equal(
            30,
            secondPayload.RootElement.GetProperty("completedDurationSeconds").GetInt32());
    }

    [Fact]
    public async Task Cancel_CompletedSession_ReturnsConflict()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        using var completeResponse = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/complete",
            null);

        using var response = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/cancel",
            null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_RunningSession_ReturnsCancelledWithElapsedSeconds()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        timeProvider.Advance(TimeSpan.FromSeconds(42));

        using var response = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/cancel",
            null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.Equal("Cancelled", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal(42, payload.RootElement.GetProperty("completedDurationSeconds").GetInt32());
    }

    [Fact]
    public async Task Cancel_CancelledSession_IsIdempotent()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        using var firstResponse = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/cancel",
            null);
        using var firstPayload = await ReadJsonAsync(firstResponse);
        var firstEndedAt = firstPayload.RootElement
            .GetProperty("endedAtUtc")
            .GetDateTimeOffset();
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        using var secondResponse = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/cancel",
            null);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var secondPayload = await ReadJsonAsync(secondResponse);
        Assert.Equal(
            firstEndedAt,
            secondPayload.RootElement.GetProperty("endedAtUtc").GetDateTimeOffset());
        Assert.Equal(
            20,
            secondPayload.RootElement.GetProperty("completedDurationSeconds").GetInt32());
    }

    [Fact]
    public async Task Complete_CancelledSession_ReturnsConflict()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        using var cancelResponse = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/cancel",
            null);

        using var response = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/complete",
            null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Start_WithInvalidPlannedDuration_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await StartAsync(client, 4);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_ReturnsOnlyOwnersSessions()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var owner = await RegisterAsync(client, "Student");
        var otherStudent = await RegisterAsync(client, "Student");
        Authorize(client, owner.AccessToken);
        using var ownerStartResponse = await StartAsync(client);
        var ownerSessionId = await ReadSessionIdAsync(ownerStartResponse);
        Authorize(client, otherStudent.AccessToken);
        using var otherStartResponse = await StartAsync(client);
        Authorize(client, owner.AccessToken);

        using var response = await client.GetAsync("/api/study-sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        var items = payload.RootElement.GetProperty("items");
        Assert.Equal(1, payload.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Single(items.EnumerateArray());
        Assert.Equal(ownerSessionId, items[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task History_OrdersNewestSessionFirst()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        var firstId = await CreateCancelledSessionAsync(client);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        using var secondStartResponse = await StartAsync(client);
        var secondId = await ReadSessionIdAsync(secondStartResponse);

        using var response = await client.GetAsync("/api/study-sessions");

        using var payload = await ReadJsonAsync(response);
        var items = payload.RootElement.GetProperty("items");
        Assert.Equal(secondId, items[0].GetProperty("id").GetGuid());
        Assert.Equal(firstId, items[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task History_AppliesDatabasePagination()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        _ = await CreateCancelledSessionAsync(client);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var middleId = await CreateCancelledSessionAsync(client);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        _ = await CreateCancelledSessionAsync(client);

        using var response = await client.GetAsync(
            "/api/study-sessions?page=2&pageSize=1");

        using var payload = await ReadJsonAsync(response);
        Assert.Equal(2, payload.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, payload.RootElement.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(
            payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(middleId, item.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task History_FiltersByStatus()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var completedStartResponse = await StartAsync(client);
        var completedId = await ReadSessionIdAsync(completedStartResponse);
        using var completeResponse = await client.PostAsync(
            $"/api/study-sessions/{completedId}/complete",
            null);
        _ = await CreateCancelledSessionAsync(client);

        using var response = await client.GetAsync(
            "/api/study-sessions?status=Completed");

        using var payload = await ReadJsonAsync(response);
        var item = Assert.Single(
            payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(completedId, item.GetProperty("id").GetGuid());
        Assert.Equal("Completed", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task History_AppliesInclusiveFromAndExclusiveToFilters()
    {
        var timeProvider = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        _ = await CreateCancelledSessionAsync(client);
        timeProvider.Advance(TimeSpan.FromHours(1));
        var includedId = await CreateCancelledSessionAsync(client);
        timeProvider.Advance(TimeSpan.FromHours(1));
        _ = await CreateCancelledSessionAsync(client);
        var fromUtc = Uri.EscapeDataString(
            "2026-10-12T11:00:00.0000000+00:00");
        var toUtc = Uri.EscapeDataString(
            "2026-10-12T12:00:00.0000000+00:00");

        using var response = await client.GetAsync(
            $"/api/study-sessions?fromUtc={fromUtc}&toUtc={toUtc}");

        using var payload = await ReadJsonAsync(response);
        var item = Assert.Single(
            payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(includedId, item.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task History_WithInvalidRangeOrUndefinedStatus_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        var instant = Uri.EscapeDataString(
            InitialUtc.ToString("O", CultureInfo.InvariantCulture));

        using var invalidRangeResponse = await client.GetAsync(
            $"/api/study-sessions?fromUtc={instant}&toUtc={instant}");
        using var invalidStatusResponse = await client.GetAsync(
            "/api/study-sessions?status=99");

        Assert.Equal(HttpStatusCode.BadRequest, invalidRangeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);
    }

    [Fact]
    public async Task DatabaseUniqueFilteredIndex_PreventsTwoRunningSessionsForStudent()
    {
        var timeProvider = CreateTimeProvider();
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        dbContext.StudySessions.Add(CreateRunningSession(student.Id, InitialUtc));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        dbContext.StudySessions.Add(CreateRunningSession(
            student.Id,
            InitialUtc.AddSeconds(1)));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());

        Assert.Contains(
            "UX_StudySessions_StudentId_Running",
            exception.InnerException?.Message ?? exception.Message,
            StringComparison.Ordinal);
    }

    private static StudySession CreateRunningSession(
        Guid studentId,
        DateTimeOffset startedAtUtc)
    {
        return new StudySession
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            StartedAtUtc = startedAtUtc,
            PlannedDurationMinutes = 25,
            Status = StudySessionStatus.Running,
            CreatedAtUtc = startedAtUtc,
            UpdatedAtUtc = startedAtUtc
        };
    }

    private static async Task<Guid> CreateCancelledSessionAsync(HttpClient client)
    {
        using var startResponse = await StartAsync(client);
        var sessionId = await ReadSessionIdAsync(startResponse);
        using var cancelResponse = await client.PostAsync(
            $"/api/study-sessions/{sessionId}/cancel",
            null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        return sessionId;
    }

    private static async Task<SqlServerWebApplicationFactory> CreateFactoryAsync(
        TimeProvider? timeProvider = null)
    {
        var factory = new SqlServerWebApplicationFactory(timeProvider);

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

    private static AdjustableTimeProvider CreateTimeProvider()
    {
        return new AdjustableTimeProvider(InitialUtc);
    }

    private static HttpClient CreateClient(SqlServerWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private static async Task<RegisteredUser> RegisterAsync(
        HttpClient client,
        string role)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = $"session-{Guid.NewGuid():N}@example.com",
                password = ValidPassword,
                displayName = "Session Test User",
                role
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        return new RegisteredUser(
            payload.RootElement.GetProperty("user").GetProperty("id").GetGuid(),
            payload.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static void Authorize(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static Task<HttpResponseMessage> StartAsync(
        HttpClient client,
        int plannedDurationMinutes = 25)
    {
        return client.PostAsJsonAsync(
            "/api/study-sessions/start",
            new { plannedDurationMinutes });
    }

    private static async Task<Guid> ReadSessionIdAsync(
        HttpResponseMessage response)
    {
        using var payload = await ReadJsonAsync(response);
        return payload.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed record RegisteredUser(Guid Id, string AccessToken);
}
