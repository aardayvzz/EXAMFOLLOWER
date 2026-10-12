using LgsTakip.Api.Application.RecurringLessons.Results;

namespace LgsTakip.Api.Controllers;

internal static class RecurringLessonProblemDetails
{
    internal static (int Status, string Title, string Detail) Describe(RecurringLessonErrorCode code) => code switch
    {
        RecurringLessonErrorCode.InvalidInput => (400, "Invalid recurring lesson request", "Tekrar eden ders isteği geçersiz."),
        RecurringLessonErrorCode.UnsupportedSource => (400, "Unsupported lesson source", "Ders kaynağı desteklenmiyor."),
        RecurringLessonErrorCode.InvalidTimeZone => (400, "Invalid time zone", "Saat dilimi desteklenmiyor."),
        RecurringLessonErrorCode.InvalidUserIdentity => (401, "Invalid user identity", "Doğrulanmış kullanıcı kimliği geçersiz."),
        RecurringLessonErrorCode.StudentNotFound => (404, "Student not found", "Öğrenci bulunamadı."),
        RecurringLessonErrorCode.DataIntegrityViolation => (500, "Recurring lesson data integrity error", "Tekrar eden ders verisi işlenemedi."),
        _ => (500, "Recurring lesson service error", "Tekrar eden ders işlemi tamamlanamadı.")
    };
}
