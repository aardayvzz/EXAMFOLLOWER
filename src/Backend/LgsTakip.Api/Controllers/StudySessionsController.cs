using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.StudySessions.Contracts;
using LgsTakip.Api.Application.StudySessions.Interfaces;
using LgsTakip.Api.Application.StudySessions.Results;
using LgsTakip.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/study-sessions")]
[Authorize(Policy = "StudentOnly")]
public sealed class StudySessionsController(
    IStudySessionService studySessionService) : ControllerBase
{
    [HttpPost("start")]
    public async Task<ActionResult<StudySessionResponse>> Start(
        StartStudySessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await studySessionService.StartAsync(
            GetStudentIdClaim(),
            request,
            cancellationToken);

        if (result.Succeeded && result.Value is not null)
        {
            return CreatedAtAction(
                nameof(GetById),
                new { id = result.Value.Id },
                result.Value);
        }

        return ToProblemDetails(result.ErrorCode);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StudySessionResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await studySessionService.GetByIdAsync(
            GetStudentIdClaim(),
            id,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet("active")]
    public async Task<ActionResult<StudySessionResponse>> GetActive(
        CancellationToken cancellationToken)
    {
        var result = await studySessionService.GetActiveAsync(
            GetStudentIdClaim(),
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblemDetails(result.ErrorCode);
        }

        return result.Value is null
            ? NoContent()
            : Ok(result.Value);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<StudySessionResponse>> Complete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await studySessionService.CompleteAsync(
            GetStudentIdClaim(),
            id,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<StudySessionResponse>> Cancel(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await studySessionService.CancelAsync(
            GetStudentIdClaim(),
            id,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet]
    public async Task<ActionResult<StudySessionListResponse>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] StudySessionStatus? status = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var result = await studySessionService.GetHistoryAsync(
            GetStudentIdClaim(),
            page,
            pageSize,
            status,
            fromUtc,
            toUtc,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    private string? GetStudentIdClaim()
    {
        return User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private ObjectResult ToProblemDetails(StudySessionErrorCode errorCode)
    {
        var (status, title, detail) = errorCode switch
        {
            StudySessionErrorCode.InvalidInput => (
                StatusCodes.Status400BadRequest,
                "Invalid study session request",
                "Çalışma oturumu isteği geçersiz."),
            StudySessionErrorCode.InvalidUserIdentity => (
                StatusCodes.Status401Unauthorized,
                "Invalid user identity",
                "Doğrulanmış kullanıcı kimliği geçersiz."),
            StudySessionErrorCode.NotFound => (
                StatusCodes.Status404NotFound,
                "Study session not found",
                "Çalışma oturumu bulunamadı."),
            StudySessionErrorCode.ActiveSessionAlreadyExists => (
                StatusCodes.Status409Conflict,
                "Active study session already exists",
                "Öğrencinin zaten aktif bir çalışma oturumu var."),
            StudySessionErrorCode.InvalidStateTransition => (
                StatusCodes.Status409Conflict,
                "Invalid study session state transition",
                "Çalışma oturumu bu duruma geçirilemez."),
            StudySessionErrorCode.DataIntegrityViolation => (
                StatusCodes.Status500InternalServerError,
                "Study session data integrity error",
                "Çalışma oturumu verisi işlenemedi."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Study session service error",
                "Çalışma oturumu işlemi tamamlanamadı.")
        };

        return Problem(
            type: $"https://httpstatuses.com/{status}",
            title: title,
            statusCode: status,
            detail: detail,
            instance: HttpContext.Request.Path);
    }
}
