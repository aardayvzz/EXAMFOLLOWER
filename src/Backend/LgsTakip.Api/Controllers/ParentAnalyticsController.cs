using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.ParentAnalytics.Contracts;
using LgsTakip.Api.Application.ParentAnalytics.Interfaces;
using LgsTakip.Api.Application.ParentAnalytics.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/parent")]
[Authorize(Policy = "ParentOnly")]
public sealed class ParentAnalyticsController(
    IParentAnalyticsService parentAnalyticsService) : ControllerBase
{
    [HttpGet("students")]
    public async Task<ActionResult<IReadOnlyList<LinkedStudentResponse>>>
        GetLinkedStudents(CancellationToken cancellationToken)
    {
        var result = await parentAnalyticsService.GetLinkedStudentsAsync(
            GetParentIdClaim(),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet("students/{studentId:guid}/study-statistics")]
    public async Task<ActionResult<ParentStudyStatisticsResponse>>
        GetStudyStatistics(
            Guid studentId,
            [FromQuery] DateTimeOffset? fromUtc = null,
            [FromQuery] DateTimeOffset? toUtc = null,
            [FromQuery] string? timeZoneId = null,
            CancellationToken cancellationToken = default)
    {
        var result = await parentAnalyticsService.GetStudyStatisticsAsync(
            GetParentIdClaim(),
            studentId,
            fromUtc,
            toUtc,
            timeZoneId,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet("students/{studentId:guid}/study-status")]
    public async Task<ActionResult<ParentStudentStudyStatusResponse>>
        GetStudyStatus(
            Guid studentId,
            CancellationToken cancellationToken)
    {
        var result = await parentAnalyticsService.GetStudyStatusAsync(
            GetParentIdClaim(),
            studentId,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    private string? GetParentIdClaim()
    {
        return User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private ObjectResult ToProblemDetails(ParentAnalyticsErrorCode errorCode)
    {
        var (status, title, detail) = errorCode switch
        {
            ParentAnalyticsErrorCode.InvalidInput => (
                StatusCodes.Status400BadRequest,
                "Invalid parent analytics request",
                "Veli istatistik isteği geçersiz."),
            ParentAnalyticsErrorCode.InvalidDateRange => (
                StatusCodes.Status400BadRequest,
                "Invalid date range",
                "Tarih aralığı geçersiz."),
            ParentAnalyticsErrorCode.InvalidTimeZone => (
                StatusCodes.Status400BadRequest,
                "Invalid time zone",
                "Zaman dilimi geçersiz veya desteklenmiyor."),
            ParentAnalyticsErrorCode.InvalidUserIdentity => (
                StatusCodes.Status401Unauthorized,
                "Invalid user identity",
                "Doğrulanmış kullanıcı kimliği geçersiz."),
            ParentAnalyticsErrorCode.StudentNotFound => (
                StatusCodes.Status404NotFound,
                "Student not found",
                "Bağlı öğrenci bulunamadı."),
            ParentAnalyticsErrorCode.DataIntegrityViolation => (
                StatusCodes.Status500InternalServerError,
                "Study session data integrity error",
                "Çalışma oturumu verisi işlenemedi."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Parent analytics service error",
                "Veli istatistik işlemi tamamlanamadı.")
        };

        return Problem(
            type: $"https://httpstatuses.com/{status}",
            title: title,
            statusCode: status,
            detail: detail,
            instance: HttpContext.Request.Path);
    }
}
