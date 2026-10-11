using ChatApp.Api.Common.Auth;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Groups;

public record CreateGroupRequest(
    string Name,
    List<Guid> UserIds
);

public record GroupDto(
    Guid Id,
    string Name,
    List<Guid> MemberIds,
    string? LastMessageContent,
    DateTimeOffset? LastMessageAt,
    int MemberCount
);

[ApiController]
[Route("api/v1/groups")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly AppDbContext _db;

    public GroupsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<GroupDto>> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var currentUserId = User.GetUserId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails { Title = "Group name is required." });
        }

        var participantIds = request.UserIds.Distinct().ToList();
        if (!participantIds.Contains(currentUserId))
        {
            participantIds.Add(currentUserId);
        }

        if (participantIds.Count < 2)
        {
            return BadRequest(new ProblemDetails { Title = "A group must have at least 2 members." });
        }

        var conversation = new Conversation
        {
            IsGroup = true,
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            Participants = participantIds.Select(id => new ConversationParticipant
            {
                UserId = id,
                Role = id == currentUserId ? "Admin" : "Member",
                JoinedAt = DateTimeOffset.UtcNow
            }).ToList()
        };

        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGroup), new { id = conversation.Id }, new GroupDto(
            conversation.Id,
            conversation.Name!,
            participantIds,
            null,
            null,
            participantIds.Count));
    }

    [HttpGet]
    public async Task<ActionResult<List<GroupDto>>> GetGroups()
    {
        var currentUserId = User.GetUserId();

        var groups = await _db.Conversations
            .Include(c => c.Participants)
            .Include(c => c.LastMessage)
            .Where(c => c.IsGroup && c.Participants.Any(p => p.UserId == currentUserId))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync();

        var dtos = groups.Select(g => new GroupDto(
            g.Id,
            g.Name!,
            g.Participants.Select(p => p.UserId).ToList(),
            g.LastMessage?.Content,
            g.LastMessageAt,
            g.Participants.Count)).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GroupDto>> GetGroup(Guid id)
    {
        var currentUserId = User.GetUserId();

        var conversation = await _db.Conversations
            .Include(c => c.Participants)
            .Include(c => c.LastMessage)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsGroup);

        if (conversation == null)
        {
            return NotFound();
        }

        if (!conversation.Participants.Any(p => p.UserId == currentUserId))
        {
            return Forbid();
        }

        return Ok(new GroupDto(
            conversation.Id,
            conversation.Name!,
            conversation.Participants.Select(p => p.UserId).ToList(),
            conversation.LastMessage?.Content,
            conversation.LastMessageAt,
            conversation.Participants.Count));
    }

    [HttpDelete("{id:guid}/leave")]
    public async Task<ActionResult> LeaveGroup(Guid id)
    {
        var currentUserId = User.GetUserId();
        var conversation = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsGroup);

        if (conversation == null) return NotFound();

        var participant = conversation.Participants.FirstOrDefault(p => p.UserId == currentUserId);
        if (participant == null) return NotFound();

        _db.ConversationParticipants.Remove(participant);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
