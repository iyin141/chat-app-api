using ChatApp.Api.Common.Auth;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Mutes;

public record MuteDto(
    long Id,
    Guid ConversationId,
    DateTimeOffset? MutedUntil,
    DateTimeOffset CreatedAt
);

public record CreateMuteRequest(
    Guid ConversationId,
    DateTimeOffset? MutedUntil
);

[ApiController]
[Route("api/v1/mutes")]
[Authorize]
public class MutesController : ControllerBase
{
    private readonly AppDbContext _db;

    public MutesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<MuteDto>>> GetMutes()
    {
        var userId = User.GetUserId();
        var mutes = await _db.UserMutes
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new MuteDto(m.Id, m.ConversationId, m.MutedUntil, m.CreatedAt))
            .ToListAsync();

        return Ok(mutes);
    }

    [HttpPost]
    public async Task<ActionResult<MuteDto>> CreateMute([FromBody] CreateMuteRequest request)
    {
        var userId = User.GetUserId();

        var existing = await _db.UserMutes.FirstOrDefaultAsync(m => m.UserId == userId && m.ConversationId == request.ConversationId);
        
        if (existing != null)
        {
            existing.MutedUntil = request.MutedUntil;
            await _db.SaveChangesAsync();
            return Ok(new MuteDto(existing.Id, existing.ConversationId, existing.MutedUntil, existing.CreatedAt));
        }

        var mute = new UserMute
        {
            UserId = userId,
            ConversationId = request.ConversationId,
            MutedUntil = request.MutedUntil,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.UserMutes.Add(mute);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMutes), new MuteDto(mute.Id, mute.ConversationId, mute.MutedUntil, mute.CreatedAt));
    }

    [HttpDelete("{conversationId:guid}")]
    public async Task<IActionResult> RemoveMute(Guid conversationId)
    {
        var userId = User.GetUserId();
        var mute = await _db.UserMutes.FirstOrDefaultAsync(m => m.UserId == userId && m.ConversationId == conversationId);

        if (mute == null)
        {
            return NotFound();
        }

        _db.UserMutes.Remove(mute);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
