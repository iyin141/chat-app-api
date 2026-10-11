namespace ChatApp.Infrastructure.Data.Entities;

public class UserMute
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public DateTimeOffset? MutedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Profile User { get; set; } = null!;
    public Conversation Conversation { get; set; } = null!;
}
