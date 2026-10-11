namespace ChatApp.Api.Features.Stories.Dtos;

public record StoryDto(
    long Id,
    Guid AuthorId,
    string AuthorUsername,
    string AuthorDisplayName,
    string? AuthorAvatarUrl,
    string MediaUrl,
    string? Caption,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
);

public record CreateStoryRequest(
    string MediaUrl,
    string? Caption
);
