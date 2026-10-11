namespace ChatApp.Infrastructure.Data.Entities;

public class Message
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public Guid? ReceiverId { get; set; }
    public string Content { get; set; } = string.Empty;
    public long? ReplyToId { get; set; }
    public string? TempId { get; set; }
    public bool Seen { get; set; } = false;
    public DateTimeOffset? SeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation properties
    public Conversation Conversation { get; set; } = null!;
    public Profile Sender { get; set; } = null!;
    public Profile? Receiver { get; set; }
    public Message? ReplyTo { get; set; }
    public ICollection<Message> Replies { get; set; } = new List<Message>();
}
