using LgsTakip.Api.Application.ParentLinking.Results;
using Microsoft.AspNetCore.Mvc;

namespace LgsTakip.Api.Controllers;

internal static class ParentLinkProblemDetails
{
    public static ObjectResult Create(
        ControllerBase controller,
        ParentLinkErrorCode errorCode)
    {
        var (status, title, detail) = errorCode switch
        {
            ParentLinkErrorCode.InvalidInput => (
                StatusCodes.Status400BadRequest,
                "Invalid parent link request",
                "İlişkilendirme isteği geçersiz."),
            ParentLinkErrorCode.InvalidUserIdentity => (
                StatusCodes.Status401Unauthorized,
                "Invalid user identity",
                "Doğrulanmış kullanıcı kimliği geçersiz."),
            ParentLinkErrorCode.InvalidOrExpiredInvitation => (
                StatusCodes.Status400BadRequest,
                "Invalid invitation",
                "Davet token'ı geçersiz veya artık kullanılamıyor."),
            ParentLinkErrorCode.InvitationAlreadyConsumed => (
                StatusCodes.Status409Conflict,
                "Invitation already consumed",
                "Davet daha önce kullanılmış."),
            ParentLinkErrorCode.InvitationCannotBeRevoked => (
                StatusCodes.Status409Conflict,
                "Invitation cannot be revoked",
                "Davet iptal edilemez."),
            ParentLinkErrorCode.InvitationNotFound => (
                StatusCodes.Status404NotFound,
                "Invitation not found",
                "Davet bulunamadı."),
            ParentLinkErrorCode.DataConflict => (
                StatusCodes.Status409Conflict,
                "Parent link conflict",
                "İlişkilendirme işlemi başka bir işlemle çakıştı."),
            ParentLinkErrorCode.DataIntegrityViolation => (
                StatusCodes.Status500InternalServerError,
                "Parent link data integrity error",
                "İlişkilendirme verisi işlenemedi."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Parent link service error",
                "İlişkilendirme işlemi tamamlanamadı.")
        };

        return controller.Problem(
            type: $"https://httpstatuses.com/{status}",
            title: title,
            statusCode: status,
            detail: detail,
            instance: controller.HttpContext.Request.Path);
    }
}
