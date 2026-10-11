using Amazon.S3;
using ChatApp.Api.Common.Auth;
using ChatApp.Api.Features.Uploads.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Api.Features.Uploads;

[ApiController]
[Route("api/v1/uploads")]
[Authorize]
public class UploadsController : ControllerBase
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;

    private const int PresignValidityMinutes = 10;

    public UploadsController(IAmazonS3 s3, IConfiguration config)
    {
        _s3 = s3;
        _config = config;
    }

    [HttpPost("presign")]
    [EnableRateLimiting("write")]
    public async Task<ActionResult<PresignUploadResponse>> Presign([FromBody] PresignUploadRequest request)
    {
        var bucket = _config["Storage:R2:Bucket"];
        var endpoint = _config["Storage:R2:Endpoint"];
        var publicBase = _config["Storage:R2:PublicBaseUrl"];

        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(endpoint))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Storage Not Configured",
                Detail = "Object storage (R2) is not configured on the server."
            });
        }

        var userId = User.GetUserId();
        var extension = Path.GetExtension(request.FileName);
        if (extension.Length > 10)
        {
            extension = string.Empty;
        }
        var key = $"{userId}/{DateTimeOffset.UtcNow:yyyyMMdd}/{Guid.NewGuid()}{extension}";

        var uploadRequest = new Amazon.S3.Model.GetPreSignedUrlRequest
        {
            Verb = Amazon.S3.HttpVerb.PUT,
            BucketName = bucket,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(PresignValidityMinutes),
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType
        };

        string uploadUrl;
        try
        {
            uploadUrl = await _s3.GetPreSignedURLAsync(uploadRequest);
        }
        catch (AmazonS3Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Storage Error",
                Detail = "Failed to generate an upload URL."
            });
        }

        var publicUrl = string.IsNullOrEmpty(publicBase)
            ? $"{endpoint.TrimEnd('/')}/{bucket}/{key}"
            : $"{publicBase.TrimEnd('/')}/{key}";

        return Ok(new PresignUploadResponse(
            uploadUrl,
            key,
            publicUrl,
            DateTimeOffset.UtcNow.AddMinutes(PresignValidityMinutes)
        ));
    }
}
