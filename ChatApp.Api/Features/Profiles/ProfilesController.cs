using ChatApp.Api.Common.Auth;
using ChatApp.Api.Features.Profiles.Dtos;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Profiles;

[ApiController]
[Route("api/v1")]
[Authorize]
public class ProfilesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProfilesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("me")]
    public async Task<ActionResult<ProfileDto>> GetCurrentUserProfile()
    {
        var userId = User.GetUserId();
        var profile = await _db.Profiles.FindAsync(userId);

        if (profile == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Profile Not Found",
                Detail = "User profile does not exist yet."
            });
        }

        return Ok(new ProfileDto(
            profile.Id,
            profile.Username,
            profile.DisplayName,
            profile.AvatarUrl,
            profile.CreatedAt,
            profile.UpdatedAt
        ));
    }

    [HttpPost("me/profile")]
    public async Task<ActionResult<ProfileDto>> CreateProfile([FromBody] CreateProfileRequest request)
    {
        var userId = User.GetUserId();

        var existing = await _db.Profiles.FindAsync(userId);
        if (existing != null)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Profile Already Exists",
                Detail = "A profile for this user ID already exists."
            });
        }

        var usernameExists = await _db.Profiles.AnyAsync(p => p.Username == request.Username);
        if (usernameExists)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Username Taken",
                Detail = "This username is already in use."
            });
        }

        var profile = new Profile
        {
            Id = userId,
            Username = request.Username.Trim(),
            DisplayName = request.DisplayName.Trim(),
            AvatarUrl = request.AvatarUrl,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Profiles.Add(profile);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCurrentUserProfile), new ProfileDto(
            profile.Id,
            profile.Username,
            profile.DisplayName,
            profile.AvatarUrl,
            profile.CreatedAt,
            profile.UpdatedAt
        ));
    }

    [HttpPatch("me")]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = User.GetUserId();
        var profile = await _db.Profiles.FindAsync(userId);

        if (profile == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Profile Not Found"
            });
        }

        if (request.DisplayName != null)
        {
            profile.DisplayName = request.DisplayName.Trim();
        }

        if (request.AvatarUrl != null)
        {
            profile.AvatarUrl = request.AvatarUrl;
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new ProfileDto(
            profile.Id,
            profile.Username,
            profile.DisplayName,
            profile.AvatarUrl,
            profile.CreatedAt,
            profile.UpdatedAt
        ));
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserSummaryDto>>> GetUsers()
    {
        var currentUserId = User.GetUserId();

        var users = await _db.Profiles
            .AsNoTracking()
            .Where(p => p.Id != currentUserId)
            .OrderBy(p => p.Username)
            .Select(p => new UserSummaryDto(
                p.Id,
                p.Username,
                p.DisplayName,
                p.AvatarUrl,
                p.CreatedAt
            ))
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("users/search")]
    public async Task<ActionResult<List<UserSummaryDto>>> SearchUsers([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(new List<UserSummaryDto>());
        }

        var currentUserId = User.GetUserId();
        var query = q.Trim().ToLower();

        var users = await _db.Profiles
            .AsNoTracking()
            .Where(p => p.Id != currentUserId && (p.Username.ToLower().Contains(query) || p.DisplayName.ToLower().Contains(query)))
            .Take(20)
            .Select(p => new UserSummaryDto(
                p.Id,
                p.Username,
                p.DisplayName,
                p.AvatarUrl,
                p.CreatedAt
            ))
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("users/{id:guid}")]
    public async Task<ActionResult<UserSummaryDto>> GetUserById(Guid id)
    {
        var user = await _db.Profiles
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new UserSummaryDto(
                p.Id,
                p.Username,
                p.DisplayName,
                p.AvatarUrl,
                p.CreatedAt
            ))
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "User Not Found"
            });
        }

        return Ok(user);
    }
}
