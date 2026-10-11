namespace ChatApp.Infrastructure.Data.Entities;

public class ConversationParticipant
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Member";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public Conversation Conversation { get; set; } = null!;
    public Profile User { get; set; } = null!;
}
