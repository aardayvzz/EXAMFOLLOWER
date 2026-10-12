using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.RecurringLessons.Contracts;
using LgsTakip.Api.Application.RecurringLessons.Interfaces;
using LgsTakip.Api.Application.RecurringLessons.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/parent/students/{studentId:guid}/recurring-lessons")]
[Authorize(Policy = "ParentOnly")]
public sealed class ParentRecurringLessonsController(IRecurringLessonService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ParentRecurringLessonResponse>>> Get(
        Guid studentId,
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await service.GetForParentAsync(claim, studentId, cancellationToken);
        return result.Succeeded && result.Value is not null ? Ok(result.Value) : Problem(result.ErrorCode);
    }

    private ObjectResult Problem(RecurringLessonErrorCode code)
    {
        var descriptor = RecurringLessonProblemDetails.Describe(code);
        return base.Problem($"https://httpstatuses.com/{descriptor.Status}", descriptor.Title,
            descriptor.Status, descriptor.Detail, HttpContext.Request.Path);
    }
}
