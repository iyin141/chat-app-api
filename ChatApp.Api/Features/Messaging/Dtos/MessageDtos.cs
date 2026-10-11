namespace ChatApp.Api.Features.Messaging.Dtos;

public record MessageDto(
    long Id,
    Guid ConversationId,
    Guid SenderId,
    Guid? ReceiverId,
    string Content,
    long? ReplyToId,
    string? TempId,
    bool Seen,
    DateTimeOffset? SeenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt
);

public record SendMessageRequest(
    string Content,
    string? TempId,
    long? ReplyToId
);

public record UpdateMessageRequest(
    string Content
);

public record MarkReadRequest(
    long? UpToMessageId,
    long[]? MessageIds,
    string[]? TempIds
);

public record ConversationSummaryDto(
    Guid Id,
    Guid OtherUserId,
    string OtherUsername,
    string OtherDisplayName,
    string? OtherAvatarUrl,
    string? SavedName,
    long? LastMessageId,
    string? LastMessageContent,
    DateTimeOffset? LastMessageAt,
    int UnreadCount
);
