using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.ExamResults.Contracts;
using LgsTakip.Api.Application.ExamResults.Interfaces;
using LgsTakip.Api.Application.ExamResults.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/exam-results")]
[Authorize(Policy = "StudentOnly")]
public sealed class ExamResultsController(
    IExamResultService examResultService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ExamResultResponse>> Create(
        CreateExamResultRequest request,
        CancellationToken cancellationToken)
    {
        var result = await examResultService.CreateAsync(
            GetUserIdClaim(),
            request,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? CreatedAtAction(
                nameof(GetById),
                new { id = result.Value.Id },
                result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExamResultResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await examResultService.GetByIdAsync(
            GetUserIdClaim(),
            id,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet]
    public async Task<ActionResult<ExamResultListResponse>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var result = await examResultService.GetListAsync(
            GetUserIdClaim(),
            page,
            pageSize,
            fromUtc,
            toUtc,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExamResultResponse>> Update(
        Guid id,
        UpdateExamResultRequest request,
        CancellationToken cancellationToken)
    {
        var result = await examResultService.UpdateAsync(
            GetUserIdClaim(),
            id,
            request,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await examResultService.DeleteAsync(
            GetUserIdClaim(),
            id,
            cancellationToken);

        return result.Succeeded
            ? NoContent()
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
