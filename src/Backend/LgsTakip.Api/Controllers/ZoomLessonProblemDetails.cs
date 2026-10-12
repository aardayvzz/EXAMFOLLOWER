using LgsTakip.Api.Application.ZoomLessons.Results;

namespace LgsTakip.Api.Controllers;

internal static class ZoomLessonProblemDetails
{
    internal static (int Status, string Title, string Detail) Describe(
        ZoomLessonErrorCode errorCode) => errorCode switch
    {
        ZoomLessonErrorCode.InvalidInput => (
            StatusCodes.Status400BadRequest,
            "Invalid Zoom lesson request",
            "Zoom ders isteği geçersiz."),
        ZoomLessonErrorCode.InvalidDateRange => (
            StatusCodes.Status400BadRequest,
            "Invalid date range",
            "Tarih aralığı geçersiz."),
        ZoomLessonErrorCode.InvalidZoomUrl => (
            StatusCodes.Status400BadRequest,
            "Invalid Zoom URL",
            "Zoom ders bağlantısı geçersiz."),
        ZoomLessonErrorCode.UnsupportedSource => (
            StatusCodes.Status400BadRequest,
            "Unsupported lesson source",
            "Ders kaynağı desteklenmiyor."),
        ZoomLessonErrorCode.InvalidUserIdentity => (
            StatusCodes.Status401Unauthorized,
            "Invalid user identity",
            "Doğrulanmış kullanıcı kimliği geçersiz."),
        ZoomLessonErrorCode.StudentNotFound => (
            StatusCodes.Status404NotFound,
            "Student not found",
            "Öğrenci bulunamadı."),
        ZoomLessonErrorCode.LessonNotFound => (
            StatusCodes.Status404NotFound,
            "Zoom lesson not found",
            "Zoom dersi bulunamadı."),
        ZoomLessonErrorCode.DataConflict => (
            StatusCodes.Status409Conflict,
            "Zoom lesson data conflict",
            "Zoom dersi eş zamanlı bir işlem nedeniyle kaydedilemedi."),
        ZoomLessonErrorCode.DataIntegrityViolation => (
            StatusCodes.Status500InternalServerError,
            "Zoom lesson data integrity error",
            "Zoom dersi verisi işlenemedi."),
        _ => (
            StatusCodes.Status500InternalServerError,
            "Zoom lesson service error",
            "Zoom dersi işlemi tamamlanamadı.")
    };
}
