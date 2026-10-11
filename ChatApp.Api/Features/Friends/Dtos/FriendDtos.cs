namespace ChatApp.Api.Features.Friends.Dtos;

public record FriendDto(
    long Id,
    Guid FriendId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string? SavedName,
    Guid ConversationId,
    string Status,
    DateTimeOffset CreatedAt
);

public record RenameFriendRequest(
    string? SavedName
);

public record FriendRequestDto(
    long Id,
    Guid SenderId,
    string SenderUsername,
    string SenderDisplayName,
    string? SenderAvatarUrl,
    Guid ReceiverId,
    string ReceiverUsername,
    string ReceiverDisplayName,
    string? ReceiverAvatarUrl,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RespondedAt
);

public record SendFriendRequest(
    Guid ReceiverId
);
