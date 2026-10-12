using LgsTakip.Api.Application.ZoomLessons.Contracts;
using LgsTakip.Api.Application.ZoomLessons.Interfaces;
using LgsTakip.Api.Application.ZoomLessons.Results;
using LgsTakip.Api.Infrastructure.CollectorAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/collector/zoom-lessons")]
[Authorize(Policy = CollectorAuthenticationDefaults.Policy)]
public sealed class CollectorZoomLessonsController(
    IZoomLessonService zoomLessonService) : ControllerBase
{
    [HttpPost("upsert")]
    [EnableRateLimiting("CollectorIngestion")]
    public async Task<ActionResult<CollectorZoomLessonResponse>> Upsert(
        UpsertZoomLessonRequest request,
        CancellationToken cancellationToken)
    {
        var result = await zoomLessonService.UpsertAsync(
            request,
            cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            return ToProblemDetails(result.ErrorCode);
        }

        return result.Value.Created
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Ok(result.Value);
    }

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
