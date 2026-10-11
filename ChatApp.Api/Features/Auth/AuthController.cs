using System.Security.Claims;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IEmailService _emailService;

    public AuthController(
        AppDbContext db,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher,
        ITokenBlacklistService tokenBlacklistService,
        IEmailService emailService)
    {
        _db = db;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _tokenBlacklistService = tokenBlacklistService;
        _emailService = emailService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Username))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Email, password, and username are required."
            });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == normalizedEmail))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Email already in use",
                Detail = "A user with this email address already exists."
            });
        }

        if (await _db.Profiles.AnyAsync(p => p.Username == normalizedUsername))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Username taken",
                Detail = "A profile with this username already exists."
            });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        var profile = new Profile
        {
            Id = user.Id,
            Username = normalizedUsername,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Username.Trim() : request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Users.Add(user);
        _db.Profiles.Add(profile);
        await _db.SaveChangesAsync();

        var token = _tokenService.GenerateToken(user, profile);
        var userDto = new UserDto(user.Id, user.Email, profile.Username, profile.DisplayName, profile.AvatarUrl);

        return StatusCode(StatusCodes.Status201Created, new AuthResponse(token, userDto));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Email and password are required."
            });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user == null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Detail = "Incorrect email or password."
            });
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Detail = "Incorrect email or password."
            });
        }

        var profile = user.Profile ?? await _db.Profiles.FindAsync(user.Id);
        if (profile == null)
        {
            profile = new Profile
            {
                Id = user.Id,
                Username = user.Email.Split('@')[0],
                DisplayName = user.Email.Split('@')[0],
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.Profiles.Add(profile);
            await _db.SaveChangesAsync();
        }

        var token = _tokenService.GenerateToken(user, profile);
        var userDto = new UserDto(user.Id, user.Email, profile.Username, profile.DisplayName, profile.AvatarUrl);

        return Ok(new AuthResponse(token, userDto));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        var user = await _db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            var profileOnly = await _db.Profiles.FindAsync(userId);
            if (profileOnly == null)
            {
                return NotFound();
            }
            return Ok(new UserDto(profileOnly.Id, "", profileOnly.Username, profileOnly.DisplayName, profileOnly.AvatarUrl));
        }

        var profile = user.Profile ?? await _db.Profiles.FindAsync(user.Id);
        var displayName = profile?.DisplayName ?? user.Email;
        var username = profile?.Username ?? user.Email;
        var avatarUrl = profile?.AvatarUrl;

        return Ok(new UserDto(user.Id, user.Email, username, displayName, avatarUrl));
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        var jti = User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(jti))
        {
            var expClaim = User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Exp);
            var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
            if (long.TryParse(expClaim, out var expSeconds))
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
            }
            _tokenBlacklistService.RevokeToken(jti, expiresAt);
        }

        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new ProblemDetails { Title = "Email is required" });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user != null)
        {
            var token = _tokenService.GeneratePasswordResetToken(user);
            
            // In a real app, this should come from configuration
            var resetLink = $"{Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:3000"}/auth/recovery?token={token}&email={Uri.EscapeDataString(user.Email)}";
            
            await _emailService.SendPasswordResetEmailAsync(user.Email, resetLink);
        }

        // Always return OK so we don't leak which emails exist
        return Ok(new { message = "If your email is registered, you will receive a password reset link shortly." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new ProblemDetails { Title = "Token and new password are required" });
        }

        var userId = _tokenService.ValidatePasswordResetToken(request.Token);
        if (userId == null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid or expired token" });
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid token" });
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password has been successfully reset." });
    }
}
