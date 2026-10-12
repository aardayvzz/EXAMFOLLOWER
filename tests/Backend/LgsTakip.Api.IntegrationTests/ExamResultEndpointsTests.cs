using System.Data;
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

public sealed class ExamResultEndpointsTests
{
    private const string ValidPassword = "SecurePass123";
    private static readonly DateTimeOffset NowUtc =
        new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExamDateUtc = NowUtc.AddDays(-1);

    [Fact]
    public async Task StudentEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/exam-results");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithParentToken_ReturnsForbidden()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, parent.AccessToken);

        using var response = await CreateResultAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidStudentRequest_ReturnsCreated()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(client, lgsScore: 430.25m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        using var json = await ReadJsonAsync(response);
        Assert.Equal("LGS Deneme 1", json.RootElement.GetProperty("examName").GetString());
        Assert.Equal(90, json.RootElement.GetProperty("totalQuestions").GetInt32());
        Assert.Equal(430.25m, json.RootElement.GetProperty("lgsScore").GetDecimal());
    }

    [Fact]
    public async Task Create_TakesStudentIdOnlyFromJwt()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var owner = await RegisterAsync(client, "Student");
        var other = await RegisterAsync(client, "Student");
        Authorize(client, owner.AccessToken);

        using var response = await client.PostAsJsonAsync("/api/exam-results", new
        {
            studentId = other.Id,
            examName = "JWT Sahiplik Testi",
            examDateUtc = ExamDateUtc,
            correctCount = 60,
            incorrectCount = 20,
            blankCount = 10,
            netScore = 999,
            createdAtUtc = DateTimeOffset.MinValue
        });
        var id = await ReadIdAsync(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        var persisted = await db.ExamResults.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(owner.Id, persisted.StudentId);
        Assert.NotEqual(other.Id, persisted.StudentId);
        Assert.NotEqual(999m, persisted.NetScore);
    }

    [Fact]
    public async Task Create_CalculatesNetScore()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(
            client,
            correctCount: 60,
            incorrectCount: 15,
            blankCount: 15);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(55m, json.RootElement.GetProperty("netScore").GetDecimal());
    }

    [Fact]
    public async Task Create_RoundsNetScoreAwayFromZeroToTwoDecimals()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(
            client,
            correctCount: 0,
            incorrectCount: 2,
            blankCount: 0);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(-0.67m, json.RootElement.GetProperty("netScore").GetDecimal());
    }

    [Fact]
    public async Task Create_WithoutLgsScore_SucceedsWithNullScore()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(client);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lgsScore").ValueKind);
    }

    [Fact]
    public async Task Create_WithNegativeLgsScore_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(client, lgsScore: -0.01m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithLgsScoreOverFiveHundred_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(client, lgsScore: 500.01m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnyNegativeQuestionCount_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var correct = await CreateResultAsync(client, correctCount: -1, incorrectCount: 1, blankCount: 1);
        using var incorrect = await CreateResultAsync(client, correctCount: 1, incorrectCount: -1, blankCount: 1);
        using var blank = await CreateResultAsync(client, correctCount: 1, incorrectCount: 1, blankCount: -1);

        Assert.Equal(HttpStatusCode.BadRequest, correct.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, incorrect.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task Create_WithZeroTotalQuestions_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(client, correctCount: 0, incorrectCount: 0, blankCount: 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithMoreThanNinetyQuestions_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(client, correctCount: 90, incorrectCount: 1, blankCount: 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDateBeyondTolerance_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var response = await CreateResultAsync(client, examDateUtc: NowUtc.AddMinutes(6));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_AsDifferentStudent_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var owner = await RegisterAsync(client, "Student");
        var other = await RegisterAsync(client, "Student");
        Authorize(client, owner.AccessToken);
        using var create = await CreateResultAsync(client);
        var id = await ReadIdAsync(create);
        Authorize(client, other.AccessToken);

        using var response = await client.GetAsync($"/api/exam-results/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OwnResult_UpdatesEditableFields()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var create = await CreateResultAsync(client);
        var id = await ReadIdAsync(create);

        using var response = await UpdateResultAsync(client, id, "Güncel Deneme", 50, 30, 10, 410m);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Güncel Deneme", json.RootElement.GetProperty("examName").GetString());
        Assert.Equal(410m, json.RootElement.GetProperty("lgsScore").GetDecimal());
    }

    [Fact]
    public async Task Update_RecalculatesNetScore()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var create = await CreateResultAsync(client);
        var id = await ReadIdAsync(create);

        using var response = await UpdateResultAsync(client, id, "Net Güncel", 70, 12, 8, null);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(66m, json.RootElement.GetProperty("netScore").GetDecimal());
    }

    [Fact]
    public async Task Update_PreservesCreatedAtUtcAndChangesUpdatedAtUtc()
    {
        var timeProvider = new AdjustableTimeProvider(NowUtc);
        await using var factory = await CreateFactoryAsync(timeProvider);
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var create = await CreateResultAsync(client);
        using var createdJson = await ReadJsonAsync(create);
        var id = createdJson.RootElement.GetProperty("id").GetGuid();
        var createdAt = createdJson.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset();
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        using var update = await UpdateResultAsync(client, id, "Zaman Güncel", 60, 20, 10, null);
        using var updatedJson = await ReadJsonAsync(update);

        Assert.Equal(createdAt, updatedJson.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal(NowUtc.AddMinutes(1), updatedJson.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task Delete_OwnResult_RemovesIt()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var create = await CreateResultAsync(client);
        var id = await ReadIdAsync(create);

        using var delete = await client.DeleteAsync($"/api/exam-results/{id}");
        using var get = await client.GetAsync($"/api/exam-results/{id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task UpdateAndDelete_AsDifferentStudent_ReturnNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var owner = await RegisterAsync(client, "Student");
        var other = await RegisterAsync(client, "Student");
        Authorize(client, owner.AccessToken);
        using var create = await CreateResultAsync(client);
        var id = await ReadIdAsync(create);
        Authorize(client, other.AccessToken);

        using var update = await UpdateResultAsync(client, id, "Yetkisiz", 60, 20, 10, null);
        using var delete = await client.DeleteAsync($"/api/exam-results/{id}");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyAuthenticatedStudentsResults()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var first = await RegisterAsync(client, "Student");
        var second = await RegisterAsync(client, "Student");
        Authorize(client, first.AccessToken);
        using var firstCreate = await CreateResultAsync(client, examName: "Birinci Öğrenci");
        Authorize(client, second.AccessToken);
        using var secondCreate = await CreateResultAsync(client, examName: "İkinci Öğrenci");
        Authorize(client, first.AccessToken);

        using var response = await client.GetAsync("/api/exam-results");
        using var json = await ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("items");

        Assert.Single(items.EnumerateArray());
        Assert.Equal("Birinci Öğrenci", items[0].GetProperty("examName").GetString());
    }

    [Fact]
    public async Task List_OrdersByExamDateDescendingThenIdAscending()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var sameDate = ExamDateUtc;
        var lowId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var highId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await AddResultAsync(factory, student.Id, highId, sameDate, "High Id");
        await AddResultAsync(factory, student.Id, lowId, sameDate, "Low Id");
        await AddResultAsync(factory, student.Id, Guid.NewGuid(), sameDate.AddDays(1), "Newest");
        Authorize(client, student.AccessToken);

        using var response = await client.GetAsync("/api/exam-results");
        using var json = await ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("items");

        Assert.Equal("Newest", items[0].GetProperty("examName").GetString());
        Assert.Equal(lowId, items[1].GetProperty("id").GetGuid());
        Assert.Equal(highId, items[2].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_AppliesPaginationAndTotalCount()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        for (var day = 1; day <= 3; day++)
        {
            await AddResultAsync(factory, student.Id, Guid.NewGuid(), ExamDateUtc.AddDays(-day), $"Deneme {day}");
        }
        Authorize(client, student.AccessToken);

        using var response = await client.GetAsync("/api/exam-results?page=2&pageSize=2");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(2, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task List_AppliesInclusiveFromAndExclusiveToFilters()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var from = ExamDateUtc.AddDays(-2);
        var to = ExamDateUtc;
        await AddResultAsync(factory, student.Id, Guid.NewGuid(), from.AddTicks(-1), "Before");
        await AddResultAsync(factory, student.Id, Guid.NewGuid(), from, "At From");
        await AddResultAsync(factory, student.Id, Guid.NewGuid(), to.AddTicks(-1), "Before To");
        await AddResultAsync(factory, student.Id, Guid.NewGuid(), to, "At To");
        Authorize(client, student.AccessToken);

        using var rangeResponse = await client.GetAsync($"/api/exam-results?fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}");
        using var fromOnlyResponse = await client.GetAsync($"/api/exam-results?fromUtc={Uri.EscapeDataString(from.ToString("O"))}");
        using var toOnlyResponse = await client.GetAsync($"/api/exam-results?toUtc={Uri.EscapeDataString(to.ToString("O"))}");
        using var rangeJson = await ReadJsonAsync(rangeResponse);

        Assert.Equal(2, rangeJson.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, fromOnlyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, toOnlyResponse.StatusCode);
    }

    [Fact]
    public async Task List_WithInvalidOrExcessiveDateRange_ReturnsBadRequest()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        var from = ExamDateUtc;
        var same = RangePath(from, from);
        var excessive = RangePath(from, from.AddDays(731));

        using var sameResponse = await client.GetAsync(same);
        using var excessiveResponse = await client.GetAsync(excessive);

        Assert.Equal(HttpStatusCode.BadRequest, sameResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, excessiveResponse.StatusCode);
    }

    [Fact]
    public async Task Parent_CanListAndGetLinkedStudentsResults()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        var student = await RegisterAsync(client, "Student");
        await LinkAsync(factory, parent.Id, student.Id);
        Authorize(client, student.AccessToken);
        using var create = await CreateResultAsync(client);
        var id = await ReadIdAsync(create);
        Authorize(client, parent.AccessToken);

        using var list = await client.GetAsync($"/api/parent/students/{student.Id}/exam-results");
        using var get = await client.GetAsync($"/api/parent/students/{student.Id}/exam-results/{id}");
        using var listJson = await ReadJsonAsync(list);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, listJson.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Parent_ForUnlinkedStudent_ReturnsNotFound()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);
        using var create = await CreateResultAsync(client);
        var id = await ReadIdAsync(create);
        Authorize(client, parent.AccessToken);

        using var list = await client.GetAsync($"/api/parent/students/{student.Id}/exam-results");
        using var get = await client.GetAsync($"/api/parent/students/{student.Id}/exam-results/{id}");

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Parent_CannotReadAnotherStudentsResultThroughLinkedStudentRoute()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var parent = await RegisterAsync(client, "Parent");
        var linked = await RegisterAsync(client, "Student");
        var other = await RegisterAsync(client, "Student");
        await LinkAsync(factory, parent.Id, linked.Id);
        Authorize(client, other.AccessToken);
        using var create = await CreateResultAsync(client);
        var otherResultId = await ReadIdAsync(create);
        Authorize(client, parent.AccessToken);

        using var response = await client.GetAsync($"/api/parent/students/{linked.Id}/exam-results/{otherResultId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Parent_CannotCreateUpdateOrDeleteStudentResults()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        var parent = await RegisterAsync(client, "Parent");
        Authorize(client, student.AccessToken);
        using var createAsStudent = await CreateResultAsync(client);
        var id = await ReadIdAsync(createAsStudent);
        Authorize(client, parent.AccessToken);

        using var create = await CreateResultAsync(client);
        using var update = await UpdateResultAsync(client, id, "Yetkisiz", 60, 20, 10, null);
        using var delete = await client.DeleteAsync($"/api/exam-results/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Responses_DoNotExposeStudentIdPasswordHashOrNavigationData()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var student = await RegisterAsync(client, "Student");
        Authorize(client, student.AccessToken);

        using var create = await CreateResultAsync(client);
        var body = await create.Content.ReadAsStringAsync();

        Assert.DoesNotContain("studentId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("student\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parents", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_CreatesExpectedCompositeIndex()
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
            JOIN sys.index_columns ic1
              ON ic1.object_id = i.object_id
             AND ic1.index_id = i.index_id
             AND ic1.key_ordinal = 1
            JOIN sys.columns c1
              ON c1.object_id = ic1.object_id
             AND c1.column_id = ic1.column_id
            JOIN sys.index_columns ic2
              ON ic2.object_id = i.object_id
             AND ic2.index_id = i.index_id
             AND ic2.key_ordinal = 2
            JOIN sys.columns c2
              ON c2.object_id = ic2.object_id
             AND c2.column_id = ic2.column_id
            WHERE i.object_id = OBJECT_ID(N'ExamResults')
              AND i.name = N'IX_ExamResults_StudentId_ExamDateUtc'
              AND i.is_unique = 0
              AND c1.name = N'StudentId'
              AND c2.name = N'ExamDateUtc';
            """;

        var count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(1, count);
    }

    private static async Task<SqlServerWebApplicationFactory> CreateFactoryAsync(
        TimeProvider? timeProvider = null)
    {
        var factory = new SqlServerWebApplicationFactory(
            timeProvider ?? new AdjustableTimeProvider(NowUtc));

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

    private static async Task<RegisteredUser> RegisterAsync(
        HttpClient client,
        string role)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"exam-{Guid.NewGuid():N}@example.com",
            password = ValidPassword,
            displayName = $"{role} Exam Test",
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

    private static Task<HttpResponseMessage> CreateResultAsync(
        HttpClient client,
        string examName = "LGS Deneme 1",
        DateTimeOffset? examDateUtc = null,
        int correctCount = 60,
        int incorrectCount = 20,
        int blankCount = 10,
        decimal? lgsScore = null) =>
        client.PostAsJsonAsync("/api/exam-results", new
        {
            examName,
            examDateUtc = examDateUtc ?? ExamDateUtc,
            correctCount,
            incorrectCount,
            blankCount,
            lgsScore
        });

    private static Task<HttpResponseMessage> UpdateResultAsync(
        HttpClient client,
        Guid id,
        string examName,
        int correctCount,
        int incorrectCount,
        int blankCount,
        decimal? lgsScore) =>
        client.PutAsJsonAsync($"/api/exam-results/{id}", new
        {
            examName,
            examDateUtc = ExamDateUtc,
            correctCount,
            incorrectCount,
            blankCount,
            lgsScore
        });

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("id").GetGuid();
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

    private static async Task AddResultAsync(
        SqlServerWebApplicationFactory factory,
        Guid studentId,
        Guid id,
        DateTimeOffset examDateUtc,
        string examName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();
        db.ExamResults.Add(new ExamResult
        {
            Id = id,
            StudentId = studentId,
            ExamName = examName,
            ExamDateUtc = examDateUtc,
            CorrectCount = 60,
            IncorrectCount = 20,
            BlankCount = 10,
            NetScore = 53.33m,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc
        });
        await db.SaveChangesAsync();
    }

    private static string RangePath(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        $"/api/exam-results?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";

    private sealed record RegisteredUser(Guid Id, string AccessToken);
}
