namespace ChatApp.Infrastructure.Data.Entities;

public class UserSettings
{
    public Guid UserId { get; set; }
    public bool GlobalNotifications { get; set; } = true;
    public string PrivacyLevel { get; set; } = "Anyone";
    public string Theme { get; set; } = "system";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Profile User { get; set; } = null!;
}
