namespace ChatApp.Infrastructure.Data.Entities;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserLowId { get; set; }
    public Guid? UserHighId { get; set; }
    public string? LegacyConvoId { get; set; }
    public bool IsGroup { get; set; } = false;
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public long? LastMessageId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Profile? UserLow { get; set; }
    public Profile? UserHigh { get; set; }
    public Message? LastMessage { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Friendship> Friendships { get; set; } = new List<Friendship>();
    public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
}
