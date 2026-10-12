using LgsTakip.Api.Domain.Enums;

namespace LgsTakip.Api.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<User> Parents { get; set; } = [];
    public ICollection<User> Students { get; set; } = [];
    public ICollection<StudySession> StudySessions { get; set; } = [];
    public ICollection<ZoomLesson> ZoomLessons { get; set; } = [];
    public ICollection<ExamResult> ExamResults { get; set; } = [];
}
