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
[Route("api/v1/friends")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub, IChatClient> _hub;

    public FriendsController(AppDbContext db, IHubContext<ChatHub, IChatClient> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpGet]
    public async Task<ActionResult<List<FriendDto>>> GetFriends()
    {
        var currentUserId = User.GetUserId();

        var friends = await _db.Friendships
            .AsNoTracking()
            .Where(f => f.UserId == currentUserId && f.Status == "active")
            .Include(f => f.Friend)
            .OrderBy(f => f.SavedName ?? f.Friend.DisplayName)
            .Select(f => new FriendDto(
                f.Id,
                f.FriendId,
                f.Friend.Username,
                f.Friend.DisplayName,
                f.Friend.AvatarUrl,
                f.SavedName,
                f.ConversationId,
                f.Status,
                f.CreatedAt
            ))
            .ToListAsync();

        return Ok(friends);
    }

    [HttpPatch("{id:long}")]
    public async Task<ActionResult<FriendDto>> RenameFriend(long id, [FromBody] RenameFriendRequest request)
    {
        var currentUserId = User.GetUserId();

        var friendship = await _db.Friendships
            .Include(f => f.Friend)
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == currentUserId);

        if (friendship == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Friendship Not Found"
            });
        }

        friendship.SavedName = string.IsNullOrWhiteSpace(request.SavedName) ? null : request.SavedName.Trim();
        await _db.SaveChangesAsync();

        return Ok(new FriendDto(
            friendship.Id,
            friendship.FriendId,
            friendship.Friend.Username,
            friendship.Friend.DisplayName,
            friendship.Friend.AvatarUrl,
            friendship.SavedName,
            friendship.ConversationId,
            friendship.Status,
            friendship.CreatedAt
        ));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> RemoveFriend(long id)
    {
        var currentUserId = User.GetUserId();

        var friendship = await _db.Friendships
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == currentUserId);

        if (friendship == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Friendship Not Found"
            });
        }

        // Remove the reciprocal friendship row as well
        var reciprocal = await _db.Friendships
            .FirstOrDefaultAsync(f => f.UserId == friendship.FriendId && f.FriendId == currentUserId);

        _db.Friendships.Remove(friendship);
        if (reciprocal != null)
        {
            _db.Friendships.Remove(reciprocal);
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
