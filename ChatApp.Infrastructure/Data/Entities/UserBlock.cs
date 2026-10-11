namespace ChatApp.Infrastructure.Data.Entities;

public class UserBlock
{
    public long Id { get; set; }
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Profile Blocker { get; set; } = null!;
    public Profile Blocked { get; set; } = null!;
}
