using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgsTakip.Api.IntegrationTests;

public sealed class ZoomLessonEndpointsTests
{
    private const string CollectorHeader = "X-Collector-Api-Key";
    private const string ValidPassword = "SecurePass123";
    private static readonly DateTimeOffset NowUtc =
        new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Collector_WithoutHeaderOrWithQueryKey_ReturnsUnauthorized()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            $"/api/collector/zoom-lessons/upsert?apiKey={Uri.EscapeDataString(factory.CollectorApiKey)}",
            CreateRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithWrongKey_ReturnsUnauthorized()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(CollectorHeader, "incorrect-collector-key-value-000000");

        using var response = await client.PostAsJsonAsync(
            "/api/collector/zoom-lessons/upsert",
            CreateRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithMultipleKeyValues_ReturnsUnauthorized()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            CollectorHeader,
            new[] { factory.CollectorApiKey, factory.CollectorApiKey });

        using var response = await client.PostAsJsonAsync(
            "/api/collector/zoom-lessons/upsert",
            CreateRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithStudentJwtInsteadOfApiKey_IsDenied()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await client.PostAsJsonAsync(
            "/api/collector/zoom-lessons/upsert",
            CreateRequest(student.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithParentJwtInsteadOfApiKey_IsDenied()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, parent.AccessToken);

        using var response = await client.PostAsJsonAsync(
            "/api/collector/zoom-lessons/upsert",
            CreateRequest(parent.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithValidKey_CreatesLesson()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");

        using var response = await UpsertAsync(client, student.Id, source: "kurspro");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("created").GetBoolean());
        Assert.Equal("Kurspro", json.RootElement.GetProperty("lesson").GetProperty("source").GetString());
    }

    [Fact]
    public async Task Collector_RepeatedUpsert_ReturnsOk()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        const string externalId = "repeat-200";
        using var first = await UpsertAsync(client, student.Id, externalId: externalId);

        using var second = await UpsertAsync(client, student.Id, externalId: externalId);
        using var json = await ReadJsonAsync(second);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(json.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public async Task Collector_RepeatedUpsert_DoesNotCreateDuplicate()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        const string externalId = "one-row-only";
        using var first = await UpsertAsync(client, student.Id, externalId: externalId);
        using var second = await UpsertAsync(client, student.Id, externalId: externalId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        Assert.Equal(1, await db.ZoomLessons.CountAsync(x =>
            x.StudentId == student.Id
            && x.Source == "Kurspro"
            && x.ExternalId == externalId));
    }

    [Fact]
    public async Task Collector_Update_PreservesIdAndCreatedAtUtc()
    {
        var timeProvider = new AdjustableTimeProvider(NowUtc);
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var first = await UpsertAsync(client, student.Id, externalId: "preserve");
        using var firstJson = await ReadJsonAsync(first);
        var original = firstJson.RootElement.GetProperty("lesson");
        var originalId = original.GetProperty("id").GetGuid();
        var originalCreatedAt = original.GetProperty("createdAtUtc").GetDateTimeOffset();
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        using var second = await UpsertAsync(client, student.Id, externalId: "preserve", title: "Güncel Ders");
        using var secondJson = await ReadJsonAsync(second);
        var updated = secondJson.RootElement.GetProperty("lesson");

        Assert.Equal(originalId, updated.GetProperty("id").GetGuid());
        Assert.Equal(originalCreatedAt, updated.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal(NowUtc.AddMinutes(5), updated.GetProperty("updatedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task Collector_Update_ChangesOnlyMutableLessonFields()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var first = await UpsertAsync(client, student.Id, externalId: "mutable");

        using var second = await UpsertAsync(
            client,
            student.Id,
            externalId: "mutable",
            title: "Fen Canlı Ders",
            startAtUtc: NowUtc.AddDays(2),
            endAtUtc: NowUtc.AddDays(2).AddMinutes(90),
            joinUrl: "https://school.zoom.us/j/987?pwd=kept",
            lockRequired: false);
        using var json = await ReadJsonAsync(second);
        var lesson = json.RootElement.GetProperty("lesson");

        Assert.Equal("Fen Canlı Ders", lesson.GetProperty("title").GetString());
        Assert.Equal(NowUtc.AddDays(2), lesson.GetProperty("startAtUtc").GetDateTimeOffset());
        Assert.Equal("https://school.zoom.us/j/987?pwd=kept", lesson.GetProperty("joinUrl").GetString());
        Assert.False(lesson.GetProperty("lockRequired").GetBoolean());
        Assert.Equal("Kurspro", lesson.GetProperty("source").GetString());
        Assert.Equal("mutable", lesson.GetProperty("externalId").GetString());
    }

    [Fact]
    public async Task Collector_SameExternalIdentityForDifferentStudents_CreatesSeparateRows()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var firstStudent = await RegisterAsync(client, "Student");
        var secondStudent = await RegisterAsync(client, "Student");
        const string externalId = "shared-class";

        using var first = await UpsertAsync(client, firstStudent.Id, externalId: externalId);
        using var second = await UpsertAsync(client, secondStudent.Id, externalId: externalId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(await ReadLessonIdAsync(first), await ReadLessonIdAsync(second));
    }

    [Fact]
    public async Task Collector_SameStudentExternalIdentity_RemainsSingleRow()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        const string externalId = "idempotent-key";

        for (var index = 0; index < 3; index++)
        {
            using var response = await UpsertAsync(client, student.Id, externalId: externalId, title: $"Ders {index}");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        Assert.Equal(1, await db.ZoomLessons.CountAsync(x =>
            x.StudentId == student.Id && x.ExternalId == externalId));
    }

    [Fact]
    public async Task Collector_ForMissingStudent_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);

        using var response = await UpsertAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithParentAsStudentId_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var parent = await RegisterAsync(client, "Parent");

        using var response = await UpsertAsync(client, parent.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithHttpOrCredentialedZoomUrl_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");

        using var http = await UpsertAsync(client, student.Id, joinUrl: "http://zoom.us/j/1");
        using var credentialed = await UpsertAsync(client, student.Id, joinUrl: "https://user:pass@zoom.us/j/1");

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, credentialed.StatusCode);
    }

    [Fact]
    public async Task Collector_WithNonZoomHost_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");

        using var response = await UpsertAsync(client, student.Id, joinUrl: "https://zoom.us.evil.example/j/1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithInvalidOrOutOfBoundsStartTime_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");

        using var reversed = await UpsertAsync(client, student.Id, startAtUtc: NowUtc.AddHours(2), endAtUtc: NowUtc.AddHours(1));
        using var tooOld = await UpsertAsync(client, student.Id, startAtUtc: NowUtc.AddDays(-31), endAtUtc: NowUtc.AddDays(-31).AddHours(1));
        using var tooFar = await UpsertAsync(client, student.Id, startAtUtc: NowUtc.AddYears(1).AddMinutes(1), endAtUtc: NowUtc.AddYears(1).AddHours(1));

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooOld.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooFar.StatusCode);
    }

    [Fact]
    public async Task Collector_WithDurationOverEightHours_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");

        using var response = await UpsertAsync(
            client,
            student.Id,
            startAtUtc: NowUtc.AddDays(1),
            endAtUtc: NowUtc.AddDays(1).AddHours(8).AddSeconds(1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Collector_WithUnsupportedSource_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");

        using var response = await UpsertAsync(client, student.Id, source: "Calendar");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StudentSchedule_ReturnsOnlyOwnedLessons()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var first = await RegisterAsync(client, "Student");
        var second = await RegisterAsync(client, "Student");
        using var firstLesson = await UpsertAsync(client, first.Id, externalId: "first-owned");
        using var secondLesson = await UpsertAsync(client, second.Id, externalId: "second-owned");
        Authorize(client, first.AccessToken);
        client.DefaultRequestHeaders.Remove(CollectorHeader);

        using var response = await client.GetAsync("/api/zoom-lessons");
        using var json = await ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("items");

        Assert.Single(items.EnumerateArray());
        Assert.Equal(await ReadLessonIdAsync(firstLesson), items[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task StudentGetById_ForAnotherStudentsLesson_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var owner = await RegisterAsync(client, "Student");
        var other = await RegisterAsync(client, "Student");
        using var lesson = await UpsertAsync(client, owner.Id);
        var lessonId = await ReadLessonIdAsync(lesson);
        Authorize(client, other.AccessToken);
        client.DefaultRequestHeaders.Remove(CollectorHeader);

        using var response = await client.GetAsync($"/api/zoom-lessons/{lessonId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StudentSchedule_UsesOverlapRuleAndValidatesRange()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        var from = NowUtc.AddDays(1);
        var to = NowUtc.AddDays(2);
        using var overlapsStart = await UpsertAsync(client, student.Id, externalId: "overlap-start", startAtUtc: from.AddHours(-1), endAtUtc: from.AddHours(1));
        using var overlapsEnd = await UpsertAsync(client, student.Id, externalId: "overlap-end", startAtUtc: to.AddHours(-1), endAtUtc: to.AddHours(1));
        using var endsAtFrom = await UpsertAsync(client, student.Id, externalId: "ends-at-from", startAtUtc: from.AddHours(-2), endAtUtc: from);
        using var startsAtTo = await UpsertAsync(client, student.Id, externalId: "starts-at-to", startAtUtc: to, endAtUtc: to.AddHours(1));
        Authorize(client, student.AccessToken);
        client.DefaultRequestHeaders.Remove(CollectorHeader);

        using var response = await client.GetAsync(SchedulePath(from, to));
        using var invalid = await client.GetAsync(SchedulePath(from, from.AddDays(91)));
        using var json = await ReadJsonAsync(response);

        Assert.Equal(2, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task StudentCurrent_ReturnsAllAndOnlyActiveLessons()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var student = await RegisterAsync(client, "Student");
        using var activeOne = await UpsertAsync(client, student.Id, externalId: "active-one", startAtUtc: NowUtc.AddMinutes(-30), endAtUtc: NowUtc.AddMinutes(30));
        using var activeTwo = await UpsertAsync(client, student.Id, externalId: "active-two", startAtUtc: NowUtc, endAtUtc: NowUtc.AddHours(1));
        using var future = await UpsertAsync(client, student.Id, externalId: "future", startAtUtc: NowUtc.AddMinutes(1), endAtUtc: NowUtc.AddHours(1));
        Authorize(client, student.AccessToken);
        client.DefaultRequestHeaders.Remove(CollectorHeader);

        using var response = await client.GetAsync("/api/zoom-lessons/current");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.True(json.RootElement[0].GetProperty("startAtUtc").GetDateTimeOffset()
            <= json.RootElement[1].GetProperty("startAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task StudentCurrent_WithoutActiveLesson_ReturnsEmptyArray()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await client.GetAsync("/api/zoom-lessons/current");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Parent_CanReadLinkedStudentsScheduleAndCurrentLessons()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        var student = await RegisterAsync(client, "Student");
        await LinkAsync(factory, parent.Id, student.Id);
        using var lesson = await UpsertAsync(client, student.Id, startAtUtc: NowUtc.AddMinutes(-10), endAtUtc: NowUtc.AddMinutes(50));
        Authorize(client, parent.AccessToken);
        client.DefaultRequestHeaders.Remove(CollectorHeader);

        using var schedule = await client.GetAsync($"/api/parent/students/{student.Id}/zoom-lessons");
        using var current = await client.GetAsync($"/api/parent/students/{student.Id}/zoom-lessons/current");
        using var scheduleJson = await ReadJsonAsync(schedule);
        using var currentJson = await ReadJsonAsync(current);

        Assert.Single(scheduleJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Single(currentJson.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Parent_ForUnlinkedStudent_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        var student = await RegisterAsync(client, "Student");
        Authorize(client, parent.AccessToken);

        using var schedule = await client.GetAsync($"/api/parent/students/{student.Id}/zoom-lessons");
        using var current = await client.GetAsync($"/api/parent/students/{student.Id}/zoom-lessons/current");

        Assert.Equal(HttpStatusCode.NotFound, schedule.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
    }

    [Fact]
    public async Task StudentAndParentResponses_DoNotExposeInternalIdentifiers()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        var student = await RegisterAsync(client, "Student");
        await LinkAsync(factory, parent.Id, student.Id);
        using var lesson = await UpsertAsync(client, student.Id, externalId: "must-not-leak");
        Authorize(client, student.AccessToken);
        client.DefaultRequestHeaders.Remove(CollectorHeader);
        using var studentResponse = await client.GetAsync("/api/zoom-lessons");
        Authorize(client, parent.AccessToken);
        using var parentResponse = await client.GetAsync($"/api/parent/students/{student.Id}/zoom-lessons");
        var bodies = await studentResponse.Content.ReadAsStringAsync()
            + await parentResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("studentId", bodies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("externalId", bodies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must-not-leak", bodies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticationAndValidationErrors_DoNotLeakApiKeyOrJoinUrl()
    {
        var logs = new ConcurrentQueue<string>();
        await using var factory = await CreateFactoryAsync(logMessages: logs);
        using var wrongKeyClient = CreateClient(factory);
        const string wrongKey = "wrong-sensitive-collector-key-000000000000";
        wrongKeyClient.DefaultRequestHeaders.Add(CollectorHeader, wrongKey);
        using var authResponse = await wrongKeyClient.PostAsJsonAsync(
            "/api/collector/zoom-lessons/upsert",
            CreateRequest(Guid.NewGuid()));
        using var collectorClient = CreateCollectorClient(factory);
        var student = await RegisterAsync(collectorClient, "Student");
        const string invalidUrl = "https://evil.example/secret-meeting";
        using var validationResponse = await UpsertAsync(collectorClient, student.Id, joinUrl: invalidUrl);
        var output = await authResponse.Content.ReadAsStringAsync()
            + await validationResponse.Content.ReadAsStringAsync()
            + string.Join(Environment.NewLine, logs);

        Assert.DoesNotContain(wrongKey, output, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidUrl, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectorRateLimit_RequestOverLimit_ReturnsTooManyRequests()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateCollectorClient(factory);
        var statuses = new List<HttpStatusCode>();

        for (var requestNumber = 0; requestNumber < 61; requestNumber++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/collector/zoom-lessons/upsert",
                new { });
            statuses.Add(response.StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(60));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[60]);
    }

    [Fact]
    public async Task Migration_CreatesBothExpectedZoomLessonIndexes()
    {
        await using var factory = await CreateFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'ZoomLessons')
              AND (
                (i.name = N'IX_ZoomLessons_StudentId_StartAtUtc'
                 AND i.is_unique = 0)
                OR
                (i.name = N'UX_ZoomLessons_StudentId_Source_ExternalId'
                 AND i.is_unique = 1));
            """;

        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Equal(2, count);

        command.CommandText = """
            SELECT filter_definition
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'ZoomLessons')
              AND name = N'UX_ZoomLessons_StudentId_Source_ExternalId';
            """;
        var filter = Convert.ToString(await command.ExecuteScalarAsync());
        Assert.Contains("[ExternalId] IS NOT NULL", filter, StringComparison.Ordinal);

        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'ZoomLessons')
              AND name = N'IX_ZoomLessons_Source_ExternalId';
            """;
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static async Task<SqlServerWebApplicationFactory> CreateFactoryAsync(
        TimeProvider? timeProvider = null,
        ConcurrentQueue<string>? logMessages = null)
    {
        var factory = new SqlServerWebApplicationFactory(
            timeProvider ?? new AdjustableTimeProvider(NowUtc),
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

    private static HttpClient CreateClient(SqlServerWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static HttpClient CreateCollectorClient(SqlServerWebApplicationFactory factory)
    {
        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(CollectorHeader, factory.CollectorApiKey);
        return client;
    }

    private static async Task<RegisteredUser> RegisterAsync(
        HttpClient client,
        string role)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"zoom-{Guid.NewGuid():N}@example.com",
            password = ValidPassword,
            displayName = $"{role} Zoom Test",
            role
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        return new RegisteredUser(
            json.RootElement.GetProperty("user").GetProperty("id").GetGuid(),
            json.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    private static object CreateRequest(
        Guid studentId,
        string title = "Matematik Canlı Ders",
        DateTimeOffset? startAtUtc = null,
        DateTimeOffset? endAtUtc = null,
        string joinUrl = "https://tedu.zoom.us/j/123456789?pwd=example",
        string source = "Kurspro",
        string externalId = "kurspro-lesson-123",
        bool lockRequired = true)
    {
        var start = startAtUtc ?? NowUtc.AddDays(1);
        return new
        {
            studentId,
            title,
            startAtUtc = start,
            endAtUtc = endAtUtc ?? start.AddHours(1),
            joinUrl,
            source,
            externalId,
            lockRequired
        };
    }

    private static Task<HttpResponseMessage> UpsertAsync(
        HttpClient client,
        Guid studentId,
        string title = "Matematik Canlı Ders",
        DateTimeOffset? startAtUtc = null,
        DateTimeOffset? endAtUtc = null,
        string joinUrl = "https://tedu.zoom.us/j/123456789?pwd=example",
        string source = "Kurspro",
        string externalId = "kurspro-lesson-123",
        bool lockRequired = true) =>
        client.PostAsJsonAsync(
            "/api/collector/zoom-lessons/upsert",
            CreateRequest(
                studentId,
                title,
                startAtUtc,
                endAtUtc,
                joinUrl,
                source,
                externalId,
                lockRequired));

    private static async Task<Guid> ReadLessonIdAsync(HttpResponseMessage response)
    {
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("lesson").GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task LinkAsync(
        SqlServerWebApplicationFactory factory,
        Guid parentId,
        Guid studentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var parent = await db.Users.SingleAsync(user => user.Id == parentId);
        var student = await db.Users.SingleAsync(user => user.Id == studentId);
        student.Parents.Add(parent);
        await db.SaveChangesAsync();
    }

    private static string SchedulePath(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc) =>
        $"/api/zoom-lessons?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";

    private sealed record RegisteredUser(Guid Id, string AccessToken);
}
