namespace LgsTakip.Api.Application.ParentLinking.Results;

public enum ParentLinkErrorCode
{
    None,
    InvalidInput,
    InvalidUserIdentity,
    InvalidOrExpiredInvitation,
    InvitationAlreadyConsumed,
    InvitationCannotBeRevoked,
    InvitationNotFound,
    DataConflict,
    DataIntegrityViolation
}
