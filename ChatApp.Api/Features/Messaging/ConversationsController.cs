using ChatApp.Api.Common.Auth;
using ChatApp.Api.Features.Messaging.Dtos;
using ChatApp.Api.Features.Realtime;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Messaging;

[ApiController]
[Route("api/v1/conversations")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ConversationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<ConversationSummaryDto>>> GetConversations()
    {
        var currentUserId = User.GetUserId();

        var friendships = await _db.Friendships
            .AsNoTracking()
            .Where(f => f.UserId == currentUserId && f.Status == "active")
            .Include(f => f.Friend)
            .Include(f => f.Conversation)
                .ThenInclude(c => c.LastMessage)
            .ToListAsync();

        var conversationIds = friendships.Select(f => f.ConversationId).ToList();

        var unreadCounts = await _db.Messages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId) && m.SenderId != currentUserId && !m.Seen)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ConversationId, g => g.Count);

        var result = friendships.Select(f => new ConversationSummaryDto(
            f.ConversationId,
            f.FriendId,
            f.Friend.Username,
            f.Friend.DisplayName,
            f.Friend.AvatarUrl,
            f.SavedName,
            f.Conversation.LastMessageId,
            f.Conversation.LastMessage?.Content,
            f.Conversation.LastMessageAt,
            unreadCounts.GetValueOrDefault(f.ConversationId, 0)
        )).OrderByDescending(c => c.LastMessageAt ?? DateTimeOffset.MinValue).ToList();

        return Ok(result.OrderByDescending(c => c.LastMessageAt ?? DateTimeOffset.MinValue));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConversationSummaryDto>> GetConversation(Guid id)
    {
        var currentUserId = User.GetUserId();

        var friendship = await _db.Friendships
            .AsNoTracking()
            .Include(f => f.Friend)
            .Include(f => f.Conversation)
                .ThenInclude(c => c.LastMessage)
            .FirstOrDefaultAsync(f => f.ConversationId == id && f.UserId == currentUserId);

        if (friendship == null)
        {
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Conversation Not Found" });
        }

        var unreadCount = await _db.Messages
            .AsNoTracking()
            .CountAsync(m => m.ConversationId == id && m.SenderId != currentUserId && !m.Seen);

        return Ok(new ConversationSummaryDto(
            friendship.ConversationId,
            friendship.FriendId,
            friendship.Friend.Username,
            friendship.Friend.DisplayName,
            friendship.Friend.AvatarUrl,
            friendship.SavedName,
            friendship.Conversation.LastMessageId,
            friendship.Conversation.LastMessage?.Content,
            friendship.Conversation.LastMessageAt,
            unreadCount
        ));
    }

    [HttpGet("with/{userId:guid}")]
    public async Task<ActionResult<Guid>> GetConversationWithUser(Guid userId)
    {
        var currentUserId = User.GetUserId();

        var friendship = await _db.Friendships
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == currentUserId && f.FriendId == userId);

        if (friendship == null)
        {
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "No Conversation Found" });
        }

        return Ok(new { conversationId = friendship.ConversationId });
    }
}
