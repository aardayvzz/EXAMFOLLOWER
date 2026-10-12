using LgsTakip.Api.Application.ExamResults.Results;

namespace LgsTakip.Api.Controllers;

internal static class ExamResultProblemDetails
{
    internal static (int Status, string Title, string Detail) Describe(
        ExamResultErrorCode errorCode) => errorCode switch
    {
        ExamResultErrorCode.InvalidInput => (
            StatusCodes.Status400BadRequest,
            "Invalid exam result request",
            "Deneme sonucu isteği geçersiz."),
        ExamResultErrorCode.InvalidDateRange => (
            StatusCodes.Status400BadRequest,
            "Invalid date range",
            "Tarih aralığı geçersiz."),
        ExamResultErrorCode.InvalidUserIdentity => (
            StatusCodes.Status401Unauthorized,
            "Invalid user identity",
            "Doğrulanmış kullanıcı kimliği geçersiz."),
        ExamResultErrorCode.NotFound => (
            StatusCodes.Status404NotFound,
            "Exam result not found",
            "Deneme sonucu bulunamadı."),
        ExamResultErrorCode.StudentNotFound => (
            StatusCodes.Status404NotFound,
            "Student not found",
            "Öğrenci bulunamadı."),
        ExamResultErrorCode.DataIntegrityViolation => (
            StatusCodes.Status500InternalServerError,
            "Exam result data integrity error",
            "Deneme sonucu verisi işlenemedi."),
        _ => (
            StatusCodes.Status500InternalServerError,
            "Exam result service error",
            "Deneme sonucu işlemi tamamlanamadı.")
    };
}
