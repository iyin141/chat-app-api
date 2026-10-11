namespace ChatApp.Api.Features.Uploads.Dtos;

public record PresignUploadRequest(
    string FileName,
    string? ContentType
);

public record PresignUploadResponse(
    string UploadUrl,
    string Key,
    string PublicUrl,
    DateTimeOffset ExpiresAt
);
