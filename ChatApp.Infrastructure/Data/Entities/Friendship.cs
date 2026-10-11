namespace ChatApp.Infrastructure.Data.Entities;

public class Friendship
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FriendId { get; set; }
    public Guid ConversationId { get; set; }
    public string? SavedName { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Profile User { get; set; } = null!;
    public Profile Friend { get; set; } = null!;
    public Conversation Conversation { get; set; } = null!;
}
