using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LgsTakip.Api.Application.ParentLinking.Contracts;
using LgsTakip.Api.Application.ParentLinking.Interfaces;
using LgsTakip.Api.Application.ParentLinking.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LgsTakip.Api.Controllers;

[ApiController]
[Route("api/student/parent-links")]
[Authorize(Policy = "StudentOnly")]
public sealed class StudentParentLinksController(
    IParentLinkService parentLinkService) : ControllerBase
{
    [HttpPost("invitations")]
    [EnableRateLimiting("ParentLinkInvitationCreate")]
    public async Task<ActionResult<CreateParentLinkInvitationResponse>>
        CreateInvitation(CancellationToken cancellationToken)
    {
        var result = await parentLinkService.CreateInvitationAsync(
            GetUserIdClaim(),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpDelete("invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeInvitation(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var result = await parentLinkService.RevokeInvitationAsync(
            GetUserIdClaim(),
            invitationId,
            cancellationToken);

        return result.Succeeded
            ? NoContent()
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LinkedParentResponse>>>
        GetLinkedParents(CancellationToken cancellationToken)
    {
        var result = await parentLinkService.GetLinkedParentsAsync(
            GetUserIdClaim(),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Ok(result.Value)
            : ToProblemDetails(result.ErrorCode);
    }

    [HttpDelete("{parentId:guid}")]
    public async Task<IActionResult> RemoveParentLink(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        var result = await parentLinkService.RemoveParentLinkAsync(
            GetUserIdClaim(),
            parentId,
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
