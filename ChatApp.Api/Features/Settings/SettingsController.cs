using ChatApp.Api.Common.Auth;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Settings;

public record SettingsDto(
    bool GlobalNotifications,
    string PrivacyLevel,
    string Theme
);

public record UpdateSettingsRequest(
    bool? GlobalNotifications,
    string? PrivacyLevel,
    string? Theme
);

[ApiController]
[Route("api/v1/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsDto>> GetSettings()
    {
        var userId = User.GetUserId();
        var settings = await _db.UserSettings.FindAsync(userId);

        if (settings == null)
        {
            // Return defaults
            settings = new UserSettings { UserId = userId };
            _db.UserSettings.Add(settings);
            await _db.SaveChangesAsync();
        }

        return Ok(new SettingsDto(settings.GlobalNotifications, settings.PrivacyLevel, settings.Theme));
    }

    [HttpPatch]
    public async Task<ActionResult<SettingsDto>> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        var userId = User.GetUserId();
        var settings = await _db.UserSettings.FindAsync(userId);

        if (settings == null)
        {
            settings = new UserSettings { UserId = userId };
            _db.UserSettings.Add(settings);
        }

        if (request.GlobalNotifications.HasValue) settings.GlobalNotifications = request.GlobalNotifications.Value;
        if (request.PrivacyLevel != null) settings.PrivacyLevel = request.PrivacyLevel;
        if (request.Theme != null) settings.Theme = request.Theme;

        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new SettingsDto(settings.GlobalNotifications, settings.PrivacyLevel, settings.Theme));
    }
}
