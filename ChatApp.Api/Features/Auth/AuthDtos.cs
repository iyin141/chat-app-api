namespace ChatApp.Api.Features.Auth;

public record RegisterRequest(
    string Email,
    string Password,
    string Username,
    string? DisplayName = null
);

public record LoginRequest(
    string Email,
    string Password
);

public record UserDto(
    Guid Id,
    string Email,
    string Username,
    string DisplayName,
    string? AvatarUrl
);

public record AuthResponse(
    string Token,
    UserDto User
);

public record ForgotPasswordRequest(
    string Email
);

public record ResetPasswordRequest(
    string Token,
    string NewPassword
);
