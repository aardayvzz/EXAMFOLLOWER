using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.ZoomLessons.Contracts;
using LgsTakip.Api.Application.ZoomLessons.Interfaces;
using LgsTakip.Api.Application.ZoomLessons.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/parent/students/{studentId:guid}/zoom-lessons")]
[Authorize(Policy = "ParentOnly")]
public sealed class ParentZoomLessonsController(
    IZoomLessonService zoomLessonService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ZoomLessonListResponse>> GetSchedule(
        Guid studentId,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var result = await zoomLessonService.GetParentScheduleAsync(
            GetUserIdClaim(),
            studentId,
            fromUtc,
            toUtc,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet("current")]
    public async Task<ActionResult<IReadOnlyList<ZoomLessonResponse>>> GetCurrent(
        Guid studentId,
        CancellationToken cancellationToken)
    {
        var result = await zoomLessonService.GetParentCurrentAsync(
            GetUserIdClaim(),
            studentId,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    private string? GetUserIdClaim() =>
        User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private ObjectResult ToProblemDetails(ZoomLessonErrorCode errorCode)
    {
        var descriptor = ZoomLessonProblemDetails.Describe(errorCode);
        return Problem(
            type: $"https://httpstatuses.com/{descriptor.Status}",
            title: descriptor.Title,
            statusCode: descriptor.Status,
            detail: descriptor.Detail,
            instance: HttpContext.Request.Path);
    }
}
