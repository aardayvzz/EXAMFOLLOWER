namespace LgsTakip.Api.Application.ParentAnalytics.Contracts;

public sealed class LinkedStudentResponse
{
    public Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public bool IsActive { get; init; }
}
