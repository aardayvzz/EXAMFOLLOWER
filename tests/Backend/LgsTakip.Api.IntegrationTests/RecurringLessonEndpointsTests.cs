using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgsTakip.Api.IntegrationTests;

public sealed class RecurringLessonEndpointsTests
{
    private const string CollectorHeader = "X-Collector-Api-Key";
    private const string Password = "SecurePass123";
    private static readonly DateTimeOffset NowUtc = new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_WithoutCollectorKey_ReturnsUnauthorized()
    {
        await using var factory = await FactoryAsync();
        using var client = Client(factory);
        using var response = await client.PostAsJsonAsync("/api/collector/recurring-lessons/snapshot", Snapshot(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Parent")]
    public async Task Snapshot_WithJwtInsteadOfCollectorKey_IsRejected(string role)
    {
        await using var factory = await FactoryAsync();
        using var client = Client(factory);
        var user = await RegisterAsync(client, role);
        Authorize(client, user.Token);
        using var response = await client.PostAsJsonAsync("/api/collector/recurring-lessons/snapshot", Snapshot(user.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Snapshot_Valid_CreatesSchedule()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var response = await SendSnapshot(client, Snapshot(student.Id));
        using var json = await JsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("createdCount").GetInt32());
    }

    [Fact]
    public async Task Snapshot_Repeated_IsIdempotent()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var first = await SendSnapshot(client, Snapshot(student.Id));
        using var second = await SendSnapshot(client, Snapshot(student.Id));
        using var json = await JsonAsync(second);
        Assert.Equal(1, json.RootElement.GetProperty("unchangedCount").GetInt32());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>().RecurringLessonSchedules.CountAsync());
    }

    [Fact]
    public async Task Snapshot_ChangedTime_CreatesNewAndDeactivatesOld()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var first = await SendSnapshot(client, Snapshot(student.Id));
        using var second = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("changed-id", start: "19:00:00", end: "20:00:00")]));
        using var json = await JsonAsync(second);
        Assert.Equal(1, json.RootElement.GetProperty("createdCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("deactivatedCount").GetInt32());
    }

    [Fact]
    public async Task CompleteSnapshot_DeactivatesMissingSchedule()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var first = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("one"), Lesson("two", day: 2)]));
        using var second = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("one")]));
        using var json = await JsonAsync(second);
        Assert.Equal(1, json.RootElement.GetProperty("deactivatedCount").GetInt32());
    }

    [Fact]
    public async Task PartialSnapshot_DoesNotDeactivateMissingSchedule()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var first = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("one"), Lesson("two", day: 2)]));
        using var second = await SendSnapshot(client, Snapshot(student.Id, complete: false, lessons: [Lesson("one")]));
        using var json = await JsonAsync(second);
        Assert.Equal(0, json.RootElement.GetProperty("deactivatedCount").GetInt32());
    }

    [Fact]
    public async Task EmptyCompleteSnapshot_IsRejected()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var response = await SendSnapshot(client, Snapshot(student.Id, lessons: []));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateExternalId_IsRejected()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var response = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("same"), Lesson("same", day: 2)]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidWeekday_IsRejected()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var response = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("bad-day", day: 8)]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTimeRange_IsRejected()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var response = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("bad-time", start: "19:00:00", end: "18:00:00")]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnsupportedTimezone_IsRejected()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var response = await SendSnapshot(client, Snapshot(student.Id, timeZone: "UTC"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkedParent_CannotReadSchedule()
    {
        await using var factory = await FactoryAsync();
        using var client = Client(factory);
        var parent = await RegisterAsync(client, "Parent");
        var student = await RegisterAsync(client, "Student");
        Authorize(client, parent.Token);
        using var response = await client.GetAsync($"/api/parent/students/{student.Id}/recurring-lessons");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NotificationFeed_ReturnsOnlyAuthenticatedStudentSchedule()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var first = await RegisterAsync(client, "Student");
        var second = await RegisterAsync(client, "Student");
        using var one = await SendSnapshot(client, Snapshot(first.Id, lessons: [Lesson("first")]));
        using var two = await SendSnapshot(client, Snapshot(second.Id, lessons: [Lesson("second")]));
        Authorize(client, first.Token);
        using var response = await client.GetAsync("/api/lesson-notifications/upcoming?days=14");
        using var json = await JsonAsync(response);
        var items = json.RootElement.GetProperty("items");
        Assert.All(items.EnumerateArray(), item => Assert.Equal("Matematik", item.GetProperty("title").GetString()));
        Assert.DoesNotContain(items.EnumerateArray(), item => item.GetProperty("occurrenceId").GetString()!.Contains("second"));
    }

    [Fact]
    public async Task NotificationFeed_GeneratesExpectedWeeklyDate()
    {
        using var json = await GetSingleOccurrenceAsync();
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 15, 0, 0, TimeSpan.Zero), json.RootElement.GetProperty("startsAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task NotificationFeed_ConvertsIstanbulLocalTimeToUtc()
    {
        using var json = await GetSingleOccurrenceAsync();
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 16, 0, 0, TimeSpan.Zero), json.RootElement.GetProperty("endsAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task NotificationFeed_OccurrenceIdIsDeterministic()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var snapshot = await SendSnapshot(client, Snapshot(student.Id));
        Authorize(client, student.Token);
        using var first = await client.GetAsync("/api/lesson-notifications/upcoming?days=14");
        using var second = await client.GetAsync("/api/lesson-notifications/upcoming?days=14");
        using var firstJson = await JsonAsync(first);
        using var secondJson = await JsonAsync(second);
        Assert.Equal(firstJson.RootElement.GetProperty("items")[0].GetProperty("occurrenceId").GetString(), secondJson.RootElement.GetProperty("items")[0].GetProperty("occurrenceId").GetString());
    }

    [Fact]
    public async Task NotificationFeed_ExcludesStaleSchedule()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var snapshot = await SendSnapshot(client, Snapshot(student.Id, collectedAt: NowUtc.AddDays(-9)));
        Authorize(client, student.Token);
        using var response = await client.GetAsync("/api/lesson-notifications/upcoming");
        using var json = await JsonAsync(response);
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task NotificationFeed_ExcludesDisabledSchedule()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var snapshot = await SendSnapshot(client, Snapshot(student.Id, lessons: [Lesson("disabled", notification: false)]));
        Authorize(client, student.Token);
        using var response = await client.GetAsync("/api/lesson-notifications/upcoming");
        using var json = await JsonAsync(response);
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ParentResponse_ReportsStaleState()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        var student = await RegisterAsync(client, "Student");
        await LinkAsync(factory, parent.Id, student.Id);
        using var snapshot = await SendSnapshot(client, Snapshot(student.Id, collectedAt: NowUtc.AddDays(-9)));
        Authorize(client, parent.Token);
        using var response = await client.GetAsync($"/api/parent/students/{student.Id}/recurring-lessons");
        using var json = await JsonAsync(response);
        Assert.True(json.RootElement[0].GetProperty("isStale").GetBoolean());
    }

    private static async Task<JsonDocument> GetSingleOccurrenceAsync()
    {
        await using var factory = await FactoryAsync();
        using var client = CollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var snapshot = await SendSnapshot(client, Snapshot(student.Id));
        Authorize(client, student.Token);
        using var response = await client.GetAsync("/api/lesson-notifications/upcoming?days=14");
        using var body = await JsonAsync(response);
        return JsonDocument.Parse(body.RootElement.GetProperty("items")[0].GetRawText());
    }

    private static object Lesson(string externalId = "kurspro-weekly-v1:abc", int day = 1,
        string start = "18:00:00", string end = "19:00:00", bool notification = true) => new
    {
        externalId, title = externalId == "second" ? "Fen" : "Matematik", isoDayOfWeek = day,
        startLocalTime = start, endLocalTime = end, notificationEnabled = notification,
        reminderMinutesBefore = 0, lockRequired = true
    };

    private static object Snapshot(Guid studentId, bool complete = true, string timeZone = "Europe/Istanbul",
        DateTimeOffset? collectedAt = null, object[]? lessons = null) => new
    {
        studentId, source = "Kurspro", timeZoneId = timeZone, collectedAtUtc = collectedAt ?? NowUtc,
        isCompleteSnapshot = complete, lessons = lessons ?? [Lesson()]
    };

    private static Task<HttpResponseMessage> SendSnapshot(HttpClient client, object request) =>
        client.PostAsJsonAsync("/api/collector/recurring-lessons/snapshot", request);

    private static async Task<SqlServerWebApplicationFactory> FactoryAsync()
    {
        var factory = new SqlServerWebApplicationFactory(new AdjustableTimeProvider(NowUtc));
        try { await factory.InitializeDatabaseAsync(); return factory; }
        catch { await factory.DisposeAsync(); throw; }
    }

    private static HttpClient Client(SqlServerWebApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static HttpClient CollectorClient(SqlServerWebApplicationFactory factory)
    {
        var client = Client(factory);
        client.DefaultRequestHeaders.Add(CollectorHeader, factory.CollectorApiKey);
        return client;
    }

    private static async Task<UserToken> RegisterAsync(HttpClient client, string role)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"recurring-{Guid.NewGuid():N}@example.com", password = Password,
            displayName = $"{role} Recurring Test", role
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await JsonAsync(response);
        return new(json.RootElement.GetProperty("user").GetProperty("id").GetGuid(),
            json.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static async Task LinkAsync(SqlServerWebApplicationFactory factory, Guid parentId, Guid studentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var parent = await db.Users.SingleAsync(user => user.Id == parentId);
        var student = await db.Users.SingleAsync(user => user.Id == studentId);
        student.Parents.Add(parent);
        await db.SaveChangesAsync();
    }

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    private sealed record UserToken(Guid Id, string Token);
}
