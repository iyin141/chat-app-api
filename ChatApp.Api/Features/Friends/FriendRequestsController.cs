using ChatApp.Api.Common.Auth;
using ChatApp.Api.Features.Friends.Dtos;
using ChatApp.Api.Features.Realtime;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Features.Friends;

[ApiController]
[Route("api/v1/friend-requests")]
[Authorize]
public class FriendRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub, IChatClient> _hub;

    public FriendRequestsController(AppDbContext db, IHubContext<ChatHub, IChatClient> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpGet]
    public async Task<ActionResult<List<FriendRequestDto>>> GetFriendRequests([FromQuery] string? direction)
    {
        var currentUserId = User.GetUserId();

        var query = _db.FriendRequests
            .AsNoTracking()
            .Include(r => r.Sender)
            .Include(r => r.Receiver)
            .Where(r => r.Status == "pending");

        if (direction?.ToLower() == "outgoing")
        {
            query = query.Where(r => r.SenderId == currentUserId);
        }
        else
        {
            query = query.Where(r => r.ReceiverId == currentUserId);
        }

        var results = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new FriendRequestDto(
                r.Id,
                r.SenderId,
                r.Sender.Username,
                r.Sender.DisplayName,
                r.Sender.AvatarUrl,
                r.ReceiverId,
                r.Receiver.Username,
                r.Receiver.DisplayName,
                r.Receiver.AvatarUrl,
                r.Status,
                r.CreatedAt,
                r.RespondedAt
            ))
            .ToListAsync();

        return Ok(results);
    }

    [HttpPost]
    public async Task<ActionResult<FriendRequestDto>> SendFriendRequest([FromBody] SendFriendRequest request)
    {
        var currentUserId = User.GetUserId();

        if (request.ReceiverId == currentUserId)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Request",
                Detail = "You cannot send a friend request to yourself."
            });
        }

        var receiver = await _db.Profiles.FindAsync(request.ReceiverId);
        if (receiver == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "User Not Found"
            });
        }

        // Check if already friends
        var alreadyFriends = await _db.Friendships.AnyAsync(f =>
            f.UserId == currentUserId && f.FriendId == request.ReceiverId && f.Status == "active");
        if (alreadyFriends)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Already Friends",
                Detail = "You are already friends with this user."
            });
        }

        // Check for pending request in either direction
        var pendingExists = await _db.FriendRequests.AnyAsync(r =>
            ((r.SenderId == currentUserId && r.ReceiverId == request.ReceiverId) ||
             (r.SenderId == request.ReceiverId && r.ReceiverId == currentUserId)) &&
            r.Status == "pending");

        if (pendingExists)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Request Pending",
                Detail = "A pending friend request already exists between you and this user."
            });
        }

        var sender = await _db.Profiles.FindAsync(currentUserId);

        var friendRequest = new FriendRequest
        {
            SenderId = currentUserId,
            ReceiverId = request.ReceiverId,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.FriendRequests.Add(friendRequest);
        await _db.SaveChangesAsync();

        var dto = new FriendRequestDto(
            friendRequest.Id,
            friendRequest.SenderId,
            sender?.Username ?? "",
            sender?.DisplayName ?? "",
            sender?.AvatarUrl,
            receiver.Id,
            receiver.Username,
            receiver.DisplayName,
            receiver.AvatarUrl,
            friendRequest.Status,
            friendRequest.CreatedAt,
            friendRequest.RespondedAt
        );

        // Notify receiver via SignalR
        await _hub.Clients.Group($"user_{request.ReceiverId}").FriendRequestReceived(dto);

        return Ok(dto);
    }

    [HttpPost("{id:long}/accept")]
    public async Task<IActionResult> AcceptFriendRequest(long id)
    {
        var currentUserId = User.GetUserId();

        var request = await _db.FriendRequests
            .Include(r => r.Sender)
            .Include(r => r.Receiver)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
        {
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Request Not Found" });
        }

        // §2.4: only receiver can accept
        if (request.ReceiverId != currentUserId)
        {
            return Forbid();
        }

        if (request.Status != "pending")
        {
            return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Invalid State", Detail = "Request is not pending." });
        }

        request.Status = "accepted";
        request.RespondedAt = DateTimeOffset.UtcNow;

        // Ensure or create conversation
        var userLowId = request.SenderId.CompareTo(request.ReceiverId) < 0 ? request.SenderId : request.ReceiverId;
        var userHighId = request.SenderId.CompareTo(request.ReceiverId) < 0 ? request.ReceiverId : request.SenderId;

        var conversation = await _db.Conversations.FirstOrDefaultAsync(c =>
            c.UserLowId == userLowId && c.UserHighId == userHighId);

        if (conversation == null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                UserLowId = userLowId,
                UserHighId = userHighId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Conversations.Add(conversation);
        }

        // Create reciprocal friendships in one transaction
        var f1 = new Friendship
        {
            UserId = request.ReceiverId,
            FriendId = request.SenderId,
            ConversationId = conversation.Id,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var f2 = new Friendship
        {
            UserId = request.SenderId,
            FriendId = request.ReceiverId,
            ConversationId = conversation.Id,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Friendships.AddRange(f1, f2);
        await _db.SaveChangesAsync();

        // Notify sender via SignalR
        await _hub.Clients.Group($"user_{request.SenderId}").FriendshipCreated(new
        {
            friendId = currentUserId,
            conversationId = conversation.Id
        });

        return Ok(new { success = true, conversationId = conversation.Id });
    }

    [HttpPost("{id:long}/reject")]
    public async Task<IActionResult> RejectFriendRequest(long id)
    {
        var currentUserId = User.GetUserId();

        var request = await _db.FriendRequests.FindAsync(id);
        if (request == null)
        {
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Request Not Found" });
        }

        // §2.4: only receiver can reject
        if (request.ReceiverId != currentUserId)
        {
            return Forbid();
        }

        request.Status = "rejected";
        request.RespondedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> CancelFriendRequest(long id)
    {
        var currentUserId = User.GetUserId();

        var request = await _db.FriendRequests.FindAsync(id);
        if (request == null)
        {
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Request Not Found" });
        }

        // §2.4: only sender can cancel
        if (request.SenderId != currentUserId)
        {
            return Forbid();
        }

        _db.FriendRequests.Remove(request);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
