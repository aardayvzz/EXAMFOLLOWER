using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.ExamResults.Contracts;
using LgsTakip.Api.Application.ExamResults.Interfaces;
using LgsTakip.Api.Application.ExamResults.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/parent/students/{studentId:guid}/exam-results")]
[Authorize(Policy = "ParentOnly")]
public sealed class ParentExamResultsController(
    IExamResultService examResultService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ExamResultListResponse>> GetList(
        Guid studentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var result = await examResultService.GetParentListAsync(
            GetUserIdClaim(),
            studentId,
            page,
            pageSize,
            fromUtc,
            toUtc,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet("{examResultId:guid}")]
    public async Task<ActionResult<ExamResultResponse>> GetById(
        Guid studentId,
        Guid examResultId,
        CancellationToken cancellationToken)
    {
        var result = await examResultService.GetParentByIdAsync(
            GetUserIdClaim(),
            studentId,
            examResultId,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    private string? GetUserIdClaim() =>
        User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private ObjectResult ToProblemDetails(ExamResultErrorCode errorCode)
    {
        var descriptor = ExamResultProblemDetails.Describe(errorCode);
        return Problem(
            type: $"https://httpstatuses.com/{descriptor.Status}",
            title: descriptor.Title,
            statusCode: descriptor.Status,
            detail: descriptor.Detail,
            instance: HttpContext.Request.Path);
    }
}
