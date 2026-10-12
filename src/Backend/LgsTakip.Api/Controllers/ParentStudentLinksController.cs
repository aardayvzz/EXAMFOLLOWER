using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.ParentAnalytics.Contracts;
using LgsTakip.Api.Application.ParentLinking.Contracts;
using LgsTakip.Api.Application.ParentLinking.Interfaces;
using LgsTakip.Api.Application.ParentLinking.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/parent/student-links")]
[Authorize(Policy = "ParentOnly")]
public sealed class ParentStudentLinksController(
    IParentLinkService parentLinkService) : ControllerBase
{
    [HttpPost("redeem")]
    [EnableRateLimiting("ParentLinkRedeem")]
    public async Task<ActionResult<LinkedStudentResponse>> Redeem(
        RedeemParentLinkRequest request,
        CancellationToken cancellationToken)
    {
        var result = await parentLinkService.RedeemInvitationAsync(
            GetUserIdClaim(),
            request,
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpDelete("{studentId:guid}")]
    public async Task<IActionResult> RemoveStudentLink(
        Guid studentId,
        CancellationToken cancellationToken)
    {
        var result = await parentLinkService.RemoveStudentLinkAsync(
            GetUserIdClaim(),
            studentId,
            cancellationToken);

        return result.Succeeded
            ? NoContent()
            : ToProblemDetails(result.ErrorCode);
    }

    private string? GetUserIdClaim()
    {
        return User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private ObjectResult ToProblemDetails(ParentLinkErrorCode errorCode)
    {
        return ParentLinkProblemDetails.Create(this, errorCode);
    }
}
