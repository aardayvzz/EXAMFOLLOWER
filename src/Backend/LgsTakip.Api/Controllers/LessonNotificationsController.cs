using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.RecurringLessons.Contracts;
using LgsTakip.Api.Application.RecurringLessons.Interfaces;
using LgsTakip.Api.Application.RecurringLessons.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/lesson-notifications")]
[Authorize(Policy = "StudentOnly")]
public sealed class LessonNotificationsController(IRecurringLessonService service) : ControllerBase
{
    [HttpGet("upcoming")]
    public async Task<ActionResult<LessonNotificationScheduleResponse>> Upcoming(
        [FromQuery] int days = 14,
        CancellationToken cancellationToken = default)
    {
        var claim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await service.GetUpcomingAsync(claim, days, cancellationToken);
        return result.Succeeded && result.Value is not null ? Ok(result.Value) : Problem(result.ErrorCode);
    }

    private ObjectResult Problem(RecurringLessonErrorCode code)
    {
        var descriptor = RecurringLessonProblemDetails.Describe(code);
        return base.Problem($"https://httpstatuses.com/{descriptor.Status}", descriptor.Title,
            descriptor.Status, descriptor.Detail, HttpContext.Request.Path);
    }
}
