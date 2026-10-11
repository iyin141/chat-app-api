namespace ChatApp.Infrastructure.Data.Entities;

public class FriendRequest
{
    public long Id { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }

    // Navigation properties
    public Profile Sender { get; set; } = null!;
    public Profile Receiver { get; set; } = null!;
}
