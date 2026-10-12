namespace LgsTakip.Api.Domain.Entities;

public class ParentStudentInvitation
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public User Student { get; set; } = null!;
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public Guid? ConsumedByParentId { get; set; }
    public User? ConsumedByParent { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
