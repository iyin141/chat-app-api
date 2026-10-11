namespace ChatApp.Api.Features.Profiles.Dtos;

public record ProfileDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record UserSummaryDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    DateTimeOffset CreatedAt
);

public record CreateProfileRequest(
    string Username,
    string DisplayName,
    string? AvatarUrl
);

public record UpdateProfileRequest(
    string? DisplayName,
    string? AvatarUrl
);
