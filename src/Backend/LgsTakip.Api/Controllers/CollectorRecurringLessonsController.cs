using LgsTakip.Api.Application.RecurringLessons.Contracts;
using LgsTakip.Api.Application.RecurringLessons.Interfaces;
using LgsTakip.Api.Application.RecurringLessons.Results;
using LgsTakip.Api.Infrastructure.CollectorAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/collector/recurring-lessons")]
[Authorize(Policy = CollectorAuthenticationDefaults.Policy)]
public sealed class CollectorRecurringLessonsController(IRecurringLessonService service) : ControllerBase
{
    [HttpPost("snapshot")]
    [EnableRateLimiting("CollectorIngestion")]
    public async Task<ActionResult<RecurringLessonSnapshotResponse>> Snapshot(
        RecurringLessonSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ProcessSnapshotAsync(request, cancellationToken);
        return result.Succeeded && result.Value is not null ? Ok(result.Value) : Problem(result.ErrorCode);
    }

    private ObjectResult Problem(RecurringLessonErrorCode code)
    {
        var descriptor = RecurringLessonProblemDetails.Describe(code);
        return base.Problem($"https://httpstatuses.com/{descriptor.Status}", descriptor.Title,
            descriptor.Status, descriptor.Detail, HttpContext.Request.Path);
    }
}
