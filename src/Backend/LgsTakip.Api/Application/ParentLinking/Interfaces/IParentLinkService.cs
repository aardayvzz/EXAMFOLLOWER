using LgsTakip.Api.Application.ParentAnalytics.Contracts;
using LgsTakip.Api.Application.ParentLinking.Contracts;
using LgsTakip.Api.Application.ParentLinking.Results;

namespace LgsTakip.Api.Application.ParentLinking.Interfaces;

public interface IParentLinkService
{
    Task<ParentLinkResult<CreateParentLinkInvitationResponse>>
        CreateInvitationAsync(
            string? studentIdClaim,
            CancellationToken cancellationToken = default);

    Task<ParentLinkResult<bool>> RevokeInvitationAsync(
        string? studentIdClaim,
        Guid invitationId,
        CancellationToken cancellationToken = default);

    Task<ParentLinkResult<LinkedStudentResponse>> RedeemInvitationAsync(
        string? parentIdClaim,
        RedeemParentLinkRequest request,
        CancellationToken cancellationToken = default);

    Task<ParentLinkResult<IReadOnlyList<LinkedParentResponse>>>
        GetLinkedParentsAsync(
            string? studentIdClaim,
            CancellationToken cancellationToken = default);

    Task<ParentLinkResult<bool>> RemoveStudentLinkAsync(
        string? parentIdClaim,
        Guid studentId,
        CancellationToken cancellationToken = default);

    Task<ParentLinkResult<bool>> RemoveParentLinkAsync(
        string? studentIdClaim,
        Guid parentId,
        CancellationToken cancellationToken = default);
}
