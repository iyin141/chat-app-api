namespace ChatApp.Infrastructure.Data.Entities;

public class Story
{
    public long Id { get; set; }
    public Guid AuthorId { get; set; }
    public string MediaUrl { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    public Profile Author { get; set; } = null!;
}
