using ChatApp.Api.Common.Auth;
using ChatApp.Api.Features.Messaging.Dtos;
using ChatApp.Api.Features.Realtime;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Messaging;

[ApiController]
[Route("api/v1")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub, IChatClient> _hub;

    public MessagesController(AppDbContext db, IHubContext<ChatHub, IChatClient> hub)
    {
        _db = db;
        _hub = hub;
    }

    private async Task BroadcastMessageCreated(
        Conversation conversation,
        Guid senderId,
        Guid? otherUserId,
        MessageDto dto)
    {
        await _hub.Clients.Group($"conversation_{conversation.Id}").MessageCreated(dto);
        await _hub.Clients.Group($"user_{senderId}").MessageCreated(dto);

        if (conversation.IsGroup)
        {
            foreach (var participantId in conversation.Participants
                .Select(p => p.UserId)
                .Where(id => id != senderId)
                .Distinct())
            {
                await _hub.Clients.Group($"user_{participantId}").MessageCreated(dto);
            }
            return;
        }

        if (otherUserId.HasValue)
        {
            await _hub.Clients.Group($"user_{otherUserId}").MessageCreated(dto);
        }
    }

    private static MessageDto MapToDto(Message m) => new(
        m.Id,
        m.ConversationId,
        m.SenderId,
        m.ReceiverId,
        m.Content,
        m.ReplyToId,
        m.TempId,
        m.Seen,
        m.SeenAt,
        m.CreatedAt,
        m.EditedAt,
        m.DeletedAt
    );

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<List<MessageDto>>> GetMessages(
        Guid conversationId,
        [FromQuery] long? before,
        [FromQuery] int limit = 50)
    {
        var currentUserId = User.GetUserId();

        // §2.4 Authorization: verify user belongs to this conversation
        var conversation = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
        {
            return NotFound();
        }

        var belongs = conversation.IsGroup 
            ? conversation.Participants.Any(p => p.UserId == currentUserId)
            : (conversation.UserLowId == currentUserId || conversation.UserHighId == currentUserId);

        if (!belongs)
        {
            return Forbid();
        }

        var query = _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.DeletedAt == null);

        if (before.HasValue)
        {
            query = query.Where(m => m.Id < before.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.Id)
            .Take(Math.Min(limit, 100))
            .Select(m => MapToDto(m))
            .ToListAsync();

        messages.Reverse(); // Return in chronological order
        return Ok(messages);
    }

    [HttpPost("conversations/{conversationId:guid}/messages")]
    [EnableRateLimiting("write")]
    public async Task<ActionResult<MessageDto>> SendMessage(
        Guid conversationId,
        [FromBody] SendMessageRequest request)
    {
        var currentUserId = User.GetUserId();

        // §2.4: User must belong to conversation
        var conversation = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
        {
            return NotFound();
        }

        var isParticipant = conversation.IsGroup 
            ? conversation.Participants.Any(p => p.UserId == currentUserId)
            : (conversation.UserLowId == currentUserId || conversation.UserHighId == currentUserId);

        if (!isParticipant)
        {
            return Forbid();
        }

        Guid? otherUserId = null;
        if (!conversation.IsGroup)
        {
            otherUserId = conversation.UserLowId == currentUserId ? conversation.UserHighId : conversation.UserLowId;

            if (otherUserId.HasValue)
            {
                var hasBlock = await _db.UserBlocks.AnyAsync(b => 
                    (b.BlockerId == currentUserId && b.BlockedId == otherUserId.Value) || 
                    (b.BlockerId == otherUserId.Value && b.BlockedId == currentUserId));

                if (hasBlock)
                {
                    return BadRequest(new ProblemDetails 
                    { 
                        Title = "Cannot Send Message", 
                        Detail = "You cannot send messages in this conversation due to a block." 
                    });
                }
            }
        }

        // Idempotency: a retry with the same tempId returns the canonical message instead of duplicating
        if (!string.IsNullOrEmpty(request.TempId))
        {
            var existing = await _db.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.TempId == request.TempId);

            if (existing != null)
            {
                return Ok(MapToDto(existing));
            }
        }

        if (request.ReplyToId.HasValue)
        {
            var replyTargetExists = await _db.Messages
                .AnyAsync(m => m.Id == request.ReplyToId.Value && m.ConversationId == conversationId && m.DeletedAt == null);

            if (!replyTargetExists)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid Reply Target",
                    Detail = "The message being replied to does not exist in this conversation."
                });
            }
        }

        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = currentUserId,
            ReceiverId = otherUserId,
            Content = request.Content.Trim(),
            ReplyToId = request.ReplyToId,
            TempId = request.TempId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Messages.Add(message);

        // Update conversation last message in the same save.
        // Navigation property (not the FK scalar) so EF orders the insert/update
        // around the message's DB-generated identity.
        conversation.LastMessage = message;
        conversation.LastMessageAt = message.CreatedAt;
        await _db.SaveChangesAsync();

        var dto = MapToDto(message);
        await BroadcastMessageCreated(conversation, currentUserId, otherUserId, dto);

        return Ok(dto);
    }

    [HttpPatch("messages/{id:long}")]
    [EnableRateLimiting("write")]
    public async Task<ActionResult<MessageDto>> EditMessage(long id, [FromBody] UpdateMessageRequest request)
    {
        var currentUserId = User.GetUserId();

        var message = await _db.Messages.FindAsync(id);
        if (message == null || message.DeletedAt != null)
        {
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Message Not Found" });
        }

        // §2.4: Sender only
        if (message.SenderId != currentUserId)
        {
            return Forbid();
        }

        message.Content = request.Content.Trim();
        message.EditedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var dto = MapToDto(message);

        await _hub.Clients.Group($"conversation_{message.ConversationId}").MessageUpdated(dto);

        return Ok(dto);
    }

    [HttpDelete("messages/{id:long}")]
    public async Task<IActionResult> DeleteMessage(long id)
    {
        var currentUserId = User.GetUserId();

        var message = await _db.Messages.FindAsync(id);
        if (message == null)
        {
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Message Not Found" });
        }

        // §2.4: Sender only
        if (message.SenderId != currentUserId)
        {
            return Forbid();
        }

        // Soft delete
        message.DeletedAt = DateTimeOffset.UtcNow;
        message.Content = "This message was deleted";
        await _db.SaveChangesAsync();

        await _hub.Clients.Group($"conversation_{message.ConversationId}").MessageDeleted(message.Id, message.ConversationId);

        return NoContent();
    }

    [HttpPost("conversations/{conversationId:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid conversationId, [FromBody] MarkReadRequest? request)
    {
        var currentUserId = User.GetUserId();

        // §2.4: User must belong to conversation
        var belongs = await _db.Conversations
            .AnyAsync(c => c.Id == conversationId && (c.UserLowId == currentUserId || c.UserHighId == currentUserId));

        if (!belongs)
        {
            return Forbid();
        }

        // §2.4: Mark-read: receiver only, never on own messages
        var unreadQuery = _db.Messages
            .Where(m => m.ConversationId == conversationId
                && m.SenderId != currentUserId
                && !m.Seen
                && m.DeletedAt == null);

        if (request != null)
        {
            if (request.UpToMessageId.HasValue)
            {
                unreadQuery = unreadQuery.Where(m => m.Id <= request.UpToMessageId.Value);
            }
            if (request.MessageIds is { Length: > 0 })
            {
                unreadQuery = unreadQuery.Where(m => request.MessageIds.Contains(m.Id));
            }
            if (request.TempIds is { Length: > 0 })
            {
                unreadQuery = unreadQuery.Where(m => m.TempId != null && request.TempIds.Contains(m.TempId));
            }
        }

        var unread = await unreadQuery.ToListAsync();

        if (unread.Any())
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var m in unread)
            {
                m.Seen = true;
                m.SeenAt = now;
            }
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"conversation_{conversationId}").MessagesSeen(conversationId, currentUserId);
        }

        return Ok(new { markedCount = unread.Count });
    }

    [HttpGet("me/unread-count")]
    public async Task<ActionResult<int>> GetTotalUnreadCount()
    {
        var currentUserId = User.GetUserId();

        var count = await _db.Messages
            .CountAsync(m => m.ReceiverId == currentUserId && !m.Seen && m.DeletedAt == null);

        return Ok(new { totalUnread = count });
    }
}
