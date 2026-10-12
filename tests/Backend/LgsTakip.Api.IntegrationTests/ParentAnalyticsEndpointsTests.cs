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

public sealed class ParentAnalyticsEndpointsTests
{
    private const string ValidPassword = "SecurePass123";
    private static readonly DateTimeOffset GeneratedAtUtc =
        new(2026, 10, 12, 18, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task ParentEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/parent/students");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ParentEndpoint_WithStudentToken_ReturnsForbidden()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student", "Öğrenci");
        Authorize(client, student.AccessToken);

        using var response = await client.GetAsync("/api/parent/students");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStudents_ReturnsLinkedStudentsInDeterministicOrder()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent", "Veli");
        var beta = await RegisterAsync(client, "Student", "Beta");
        var alpha = await RegisterAsync(client, "Student", "Alpha");
        await LinkAsync(factory, parent.Id, beta.Id);
        await LinkAsync(factory, parent.Id, alpha.Id);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync("/api/parent/students");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.Equal(2, payload.RootElement.GetArrayLength());
        Assert.Equal(alpha.Id, payload.RootElement[0].GetProperty("id").GetGuid());
        Assert.Equal(beta.Id, payload.RootElement[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetStudents_DoesNotReturnUnlinkedStudents()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent", "Veli");
        var linked = await RegisterAsync(client, "Student", "Bağlı");
        var unlinked = await RegisterAsync(client, "Student", "İlişkisiz");
        await LinkAsync(factory, parent.Id, linked.Id);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync("/api/parent/students");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(linked.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unlinked.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStudents_WithoutLinks_ReturnsEmptyArray()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent", "Veli");
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync("/api/parent/students");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.Equal(0, payload.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetStatistics_ForLinkedStudent_ReturnsOk()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.Equal(
            student.Id,
            payload.RootElement.GetProperty("student").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetStatistics_ForUnlinkedStudent_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent", "Veli");
        var student = await RegisterAsync(client, "Student", "İlişkisiz");
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStatistics_ForStudentLinkedToAnotherParent_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var requestingParent = await RegisterAsync(client, "Parent", "İsteyen Veli");
        var otherParent = await RegisterAsync(client, "Parent", "Diğer Veli");
        var student = await RegisterAsync(client, "Student", "Öğrenci");
        await LinkAsync(factory, otherParent.Id, student.Id);
        Authorize(client, requestingParent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStudents_DoesNotListLinkedNonStudentUser()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent", "Veli");
        var linkedParent = await RegisterAsync(client, "Parent", "Bağlı Parent");
        await LinkAsync(factory, parent.Id, linkedParent.Id);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync("/api/parent/students");

        using var payload = await ReadJsonAsync(response);
        Assert.Equal(0, payload.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetStatistics_WithoutRange_UsesLastSevenLocalCalendarDays()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        using var payload = await ReadJsonAsync(response);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 5, 21, 0, 0, TimeSpan.Zero),
            payload.RootElement.GetProperty("fromUtc").GetDateTimeOffset());
        Assert.Equal(
            new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero),
            payload.RootElement.GetProperty("toUtc").GetDateTimeOffset());
        Assert.Equal("Europe/Istanbul", payload.RootElement.GetProperty("timeZoneId").GetString());
        Assert.Equal(7, payload.RootElement.GetProperty("daily").GetArrayLength());
    }

    [Fact]
    public async Task GetStatistics_WithOnlyToUtc_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(
            $"{StatisticsPath(student.Id)}?toUtc={Encode(GeneratedAtUtc)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStatistics_WithOnlyFromUtc_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(
            $"{StatisticsPath(student.Id)}?fromUtc={Encode(GeneratedAtUtc)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStatistics_WithNonIncreasingRange_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);
        var instant = Encode(GeneratedAtUtc);

        using var response = await client.GetAsync(
            $"{StatisticsPath(student.Id)}?fromUtc={instant}&toUtc={instant}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStatistics_WithRangeLongerThanNinetyThreeDays_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);
        var fromUtc = GeneratedAtUtc.AddDays(-94);

        using var response = await client.GetAsync(
            $"{StatisticsPath(student.Id)}?fromUtc={Encode(fromUtc)}&toUtc={Encode(GeneratedAtUtc)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStatistics_WithInvalidTimeZone_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(
            $"{StatisticsPath(student.Id)}?timeZoneId=Invalid%2FZone");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStatistics_CalculatesCompletedAndCancelledTotals()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Completed, GeneratedAtUtc.AddDays(-2), 25, 60);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Completed, GeneratedAtUtc.AddDays(-1), 30, 120);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Cancelled, GeneratedAtUtc.AddHours(-1), 10, 30);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        using var payload = await ReadJsonAsync(response);
        var summary = payload.RootElement.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("totalSessionCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("completedSessionCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("cancelledSessionCount").GetInt32());
        Assert.Equal(210, summary.GetProperty("totalStudySeconds").GetInt64());
        Assert.Equal(65, summary.GetProperty("plannedStudyMinutes").GetInt64());
        Assert.Equal(90m, summary.GetProperty("averageCompletedSessionSeconds").GetDecimal());
    }

    [Fact]
    public async Task GetStatistics_ExcludesRunningSessionFromSummary()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Completed, GeneratedAtUtc.AddHours(-2), 25, 60);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Running, GeneratedAtUtc.AddMinutes(-10), 25, 0);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        using var payload = await ReadJsonAsync(response);
        Assert.Equal(
            1,
            payload.RootElement.GetProperty("summary").GetProperty("totalSessionCount").GetInt32());
    }

    [Fact]
    public async Task GetStatistics_ReturnsRunningSessionAsActiveSession()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        var activeId = await AddSessionAsync(factory, student.Id, StudySessionStatus.Running, GeneratedAtUtc.AddMinutes(-10), 25, 0);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        using var payload = await ReadJsonAsync(response);
        var active = payload.RootElement.GetProperty("activeSession");
        Assert.Equal(activeId, active.GetProperty("id").GetGuid());
        Assert.Equal(600, active.GetProperty("elapsedSeconds").GetInt64());
    }

    [Fact]
    public async Task GetStatistics_CalculatesCompletionRateToTwoDecimals()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Completed, GeneratedAtUtc.AddDays(-3), 25, 60);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Completed, GeneratedAtUtc.AddDays(-2), 25, 60);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Cancelled, GeneratedAtUtc.AddDays(-1), 25, 60);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        using var payload = await ReadJsonAsync(response);
        Assert.Equal(
            66.67m,
            payload.RootElement.GetProperty("summary").GetProperty("completionRatePercent").GetDecimal());
    }

    [Fact]
    public async Task GetStatistics_WithNoSessions_ReturnsZeroAveragesAndRates()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id));

        using var payload = await ReadJsonAsync(response);
        var summary = payload.RootElement.GetProperty("summary");
        Assert.Equal(0m, summary.GetProperty("averageCompletedSessionSeconds").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("completionRatePercent").GetDecimal());
    }

    [Fact]
    public async Task GetStatistics_IncludesEmptyDaysWithZeroValues()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        var fromUtc = new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.Zero);
        var toUtc = new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Completed, fromUtc.AddDays(1).AddHours(1), 25, 60);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id, fromUtc, toUtc));

        using var payload = await ReadJsonAsync(response);
        var daily = payload.RootElement.GetProperty("daily");
        Assert.Equal(3, daily.GetArrayLength());
        Assert.Equal(0, daily[0].GetProperty("totalSessionCount").GetInt32());
        Assert.Equal(1, daily[1].GetProperty("totalSessionCount").GetInt32());
        Assert.Equal(0, daily[2].GetProperty("totalSessionCount").GetInt32());
    }

    [Fact]
    public async Task GetStatistics_OrdersDailyRowsAscending()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        var fromUtc = new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.Zero);
        var toUtc = new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id, fromUtc, toUtc));

        using var payload = await ReadJsonAsync(response);
        var dates = payload.RootElement.GetProperty("daily")
            .EnumerateArray()
            .Select(item => item.GetProperty("date").GetString()!)
            .ToArray();
        Assert.Equal(["2026-10-10", "2026-10-11", "2026-10-12"], dates);
    }

    [Fact]
    public async Task GetStatistics_AssignsUtcSessionNearMidnightToIstanbulLocalDate()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        var fromUtc = new DateTimeOffset(2026, 10, 11, 21, 0, 0, TimeSpan.Zero);
        var toUtc = new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Completed, new DateTimeOffset(2026, 10, 11, 21, 30, 0, TimeSpan.Zero), 25, 60);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatisticsPath(student.Id, fromUtc, toUtc));

        using var payload = await ReadJsonAsync(response);
        var daily = Assert.Single(payload.RootElement.GetProperty("daily").EnumerateArray());
        Assert.Equal("2026-10-12", daily.GetProperty("date").GetString());
        Assert.Equal(1, daily.GetProperty("totalSessionCount").GetInt32());
    }

    [Fact]
    public async Task GetStudyStatus_WithActiveSession_ReturnsIsStudyingTrue()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        await AddSessionAsync(factory, student.Id, StudySessionStatus.Running, GeneratedAtUtc.AddMinutes(-10), 25, 0);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatusPath(student.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.True(payload.RootElement.GetProperty("isStudying").GetBoolean());
        Assert.Equal(600, payload.RootElement.GetProperty("activeSession").GetProperty("elapsedSeconds").GetInt64());
    }

    [Fact]
    public async Task GetStudyStatus_WithoutActiveSession_ReturnsIsStudyingFalse()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync(StatusPath(student.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        Assert.False(payload.RootElement.GetProperty("isStudying").GetBoolean());
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("activeSession").ValueKind);
    }

    [Fact]
    public async Task ParentResponses_DoNotExposePasswordHashOrUnrelatedUserData()
    {
        await using var factory = await CreateFactoryAsync(CreateTimeProvider());
        using var client = CreateClient(factory);
        var (parent, student) = await RegisterAndLinkAsync(factory, client);
        var unrelated = await RegisterAsync(client, "Student", "Gizli Kullanıcı");
        Authorize(client, parent.AccessToken);

        using var listResponse = await client.GetAsync("/api/parent/students");
        using var statisticsResponse = await client.GetAsync(StatisticsPath(student.Id));
        var combinedBody = await listResponse.Content.ReadAsStringAsync()
            + await statisticsResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("passwordHash", combinedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unrelated.Id.ToString(), combinedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gizli Kullanıcı", combinedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("parents", combinedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("students", combinedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isLocked", combinedBody, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(RegisteredUser Parent, RegisteredUser Student)>
        RegisterAndLinkAsync(
            SqlServerWebApplicationFactory factory,
            HttpClient client)
    {
        var parent = await RegisterAsync(client, "Parent", "Veli");
        var student = await RegisterAsync(client, "Student", "Öğrenci");
        await LinkAsync(factory, parent.Id, student.Id);
        return (parent, student);
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

    private static async Task<Guid> AddSessionAsync(
        SqlServerWebApplicationFactory factory,
        Guid studentId,
        StudySessionStatus status,
        DateTimeOffset startedAtUtc,
        int plannedDurationMinutes,
        int completedDurationSeconds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var sessionId = Guid.NewGuid();
        var endedAtUtc = status == StudySessionStatus.Running
            ? (DateTimeOffset?)null
            : startedAtUtc.AddSeconds(completedDurationSeconds);

        dbContext.StudySessions.Add(new StudySession
        {
            Id = sessionId,
            StudentId = studentId,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            PlannedDurationMinutes = plannedDurationMinutes,
            CompletedDurationSeconds = completedDurationSeconds,
            Status = status,
            CreatedAtUtc = startedAtUtc,
            UpdatedAtUtc = endedAtUtc ?? startedAtUtc
        });
        await dbContext.SaveChangesAsync();
        return sessionId;
    }

    private static async Task<RegisteredUser> RegisterAsync(
        HttpClient client,
        string role,
        string displayName)
    {
        var email = $"parent-analytics-{Guid.NewGuid():N}@example.com";
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email,
                password = ValidPassword,
                displayName,
                role
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var payload = await ReadJsonAsync(response);

        return new RegisteredUser(
            payload.RootElement.GetProperty("user").GetProperty("id").GetGuid(),
            payload.RootElement.GetProperty("accessToken").GetString()!,
            email,
            displayName);
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
        return new AdjustableTimeProvider(GeneratedAtUtc);
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

    private static string StatisticsPath(
        Guid studentId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null)
    {
        var path = $"/api/parent/students/{studentId}/study-statistics";
        return fromUtc.HasValue && toUtc.HasValue
            ? $"{path}?fromUtc={Encode(fromUtc.Value)}&toUtc={Encode(toUtc.Value)}&timeZoneId=Europe%2FIstanbul"
            : path;
    }

    private static string StatusPath(Guid studentId)
    {
        return $"/api/parent/students/{studentId}/study-status";
    }

    private static string Encode(DateTimeOffset value)
    {
        return Uri.EscapeDataString(
            value.ToString("O", CultureInfo.InvariantCulture));
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed record RegisteredUser(
        Guid Id,
        string AccessToken,
        string Email,
        string DisplayName);
}
