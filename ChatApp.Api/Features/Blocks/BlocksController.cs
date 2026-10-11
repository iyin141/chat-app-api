using ChatApp.Api.Common.Auth;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Blocks;

public record BlockDto(
    long Id,
    Guid BlockedUserId,
    DateTimeOffset CreatedAt
);

public record CreateBlockRequest(
    Guid BlockedUserId
);

[ApiController]
[Route("api/v1/blocks")]
[Authorize]
public class BlocksController : ControllerBase
{
    private readonly AppDbContext _db;

    public BlocksController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<BlockDto>>> GetBlocks()
    {
        var userId = User.GetUserId();
        var blocks = await _db.UserBlocks
            .AsNoTracking()
            .Where(b => b.BlockerId == userId)
            .Select(b => new BlockDto(b.Id, b.BlockedId, b.CreatedAt))
            .ToListAsync();

        return Ok(blocks);
    }

    [HttpPost]
    public async Task<ActionResult<BlockDto>> CreateBlock([FromBody] CreateBlockRequest request)
    {
        var userId = User.GetUserId();

        if (userId == request.BlockedUserId)
        {
            return BadRequest(new ProblemDetails { Title = "Cannot block yourself." });
        }

        var exists = await _db.UserBlocks.AnyAsync(b => b.BlockerId == userId && b.BlockedId == request.BlockedUserId);
        if (exists)
        {
            return Conflict(new ProblemDetails { Title = "User already blocked." });
        }

        var block = new UserBlock
        {
            BlockerId = userId,
            BlockedId = request.BlockedUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.UserBlocks.Add(block);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBlocks), new BlockDto(block.Id, block.BlockedId, block.CreatedAt));
    }

    [HttpDelete("{blockedUserId:guid}")]
    public async Task<IActionResult> RemoveBlock(Guid blockedUserId)
    {
        var userId = User.GetUserId();
        var block = await _db.UserBlocks.FirstOrDefaultAsync(b => b.BlockerId == userId && b.BlockedId == blockedUserId);

        if (block == null)
        {
            return NotFound();
        }

        _db.UserBlocks.Remove(block);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
