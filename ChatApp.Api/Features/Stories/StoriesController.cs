using ChatApp.Api.Common.Auth;
using ChatApp.Api.Features.Messaging.Dtos;
using ChatApp.Api.Features.Realtime;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace ChatApp.Api.Features.Stories;

public record StoryDto(
    long Id,
    Guid AuthorId, string Username,
    string MediaUrl,
    string MediaType,
    string? Caption,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
);

public record CreateStoryRequest(
    string MediaUrl,
    string MediaType,
    string? Caption
);

[ApiController]
[Route("api/v1/stories")]
[Authorize]
public class StoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly IHubContext<ChatHub, IChatClient> _hub;

    public StoriesController(AppDbContext db, IDistributedCache cache, IHubContext<ChatHub, IChatClient> hub)
    {
        _db = db;
        _cache = cache;
        _hub = hub;
    }

    [HttpGet]
    public async Task<ActionResult<List<StoryDto>>> GetActiveStories()
    {
        var userId = User.GetUserId();
        var now = DateTimeOffset.UtcNow;

        var cacheKey = $"stories_{userId}";
        var cachedData = await _cache.GetStringAsync(cacheKey);
        
        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedStories = JsonSerializer.Deserialize<List<StoryDto>>(cachedData);
            if (cachedStories != null)
            {
                // Filter out expired stories from cache
                cachedStories = cachedStories.Where(s => s.ExpiresAt > now).ToList();
                return Ok(cachedStories);
            }
        }

        var friendsIds = await _db.Friendships
            .Where(f => f.UserId == userId && f.Status == "active")
            .Select(f => f.FriendId)
            .ToListAsync();
        
        friendsIds.Add(userId); // include own stories

        var stories = await _db.Stories.Include(s => s.Author)
            .AsNoTracking()
            .Where(s => friendsIds.Contains(s.AuthorId) && s.ExpiresAt > now)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new StoryDto(
                s.Id,
                s.AuthorId, s.Author.Username,
                s.MediaUrl,
                s.MediaType,
                s.Caption,
                s.CreatedAt,
                s.ExpiresAt
            ))
            .ToListAsync();

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(stories), cacheOptions);

        return Ok(stories);
    }

    [HttpPost]
    public async Task<ActionResult<StoryDto>> CreateStory([FromBody] CreateStoryRequest request)
    {
        var userId = User.GetUserId();

        var mediaType = string.IsNullOrWhiteSpace(request.MediaType) ? "text" : request.MediaType.Trim();
        var caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();
        var mediaUrl = request.MediaUrl?.Trim() ?? "";

        if (string.Equals(mediaType, "text", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(caption))
            {
                return BadRequest(new ProblemDetails { Title = "Caption is required for text stories" });
            }
        }
        else if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return BadRequest(new ProblemDetails { Title = "MediaUrl is required" });
        }

        var story = new Story
        {
            AuthorId = userId,
            MediaUrl = mediaUrl,
            MediaType = mediaType,
            Caption = caption,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        };

        _db.Stories.Add(story);
        await _db.SaveChangesAsync();

        // Invalidate cache for the user and their friends
        await InvalidateStoriesCache(userId);

        return CreatedAtAction(nameof(GetActiveStories), new StoryDto(
            story.Id,
            story.AuthorId, User.Identity?.Name ?? "You",
            story.MediaUrl,
            story.MediaType,
            story.Caption,
            story.CreatedAt,
            story.ExpiresAt
        ));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteStory(long id)
    {
        var userId = User.GetUserId();
        var story = await _db.Stories.FindAsync(id);

        if (story == null || story.AuthorId != userId)
        {
            return NotFound();
        }

        _db.Stories.Remove(story);
        await _db.SaveChangesAsync();

        await InvalidateStoriesCache(userId);

        return NoContent();
    }

    public record ReplyStoryRequest(string Content);

    [HttpPost("{id:long}/reply")]
    public async Task<IActionResult> ReplyStory(long id, [FromBody] ReplyStoryRequest request)
    {
        var userId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new ProblemDetails { Title = "Reply content is required" });
        }

        var story = await _db.Stories.FindAsync(id);
        if (story == null || story.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return NotFound(new ProblemDetails { Title = "Story Not Found" });
        }

        if (story.AuthorId == userId)
        {
            return BadRequest(new ProblemDetails { Title = "Cannot reply to your own story." });
        }

        var friendship = await _db.Friendships
            .Include(f => f.Conversation)
            .FirstOrDefaultAsync(f => f.UserId == userId && f.FriendId == story.AuthorId && f.Status == "active");

        if (friendship == null)
        {
            return BadRequest(new ProblemDetails { Title = "You can only reply to friends' stories." });
        }

        var conversation = friendship.Conversation;
        var content = request.Content.Trim();

        var message = new Message
        {
            ConversationId = conversation.Id,
            SenderId = userId,
            ReceiverId = story.AuthorId,
            Content = content,
            TempId = $"story-reply-{id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Messages.Add(message);
        conversation.LastMessage = message;
        conversation.LastMessageAt = message.CreatedAt;
        await _db.SaveChangesAsync();

        var dto = new MessageDto(
            message.Id,
            message.ConversationId,
            message.SenderId,
            message.ReceiverId,
            message.Content,
            message.ReplyToId,
            message.TempId,
            message.Seen,
            message.SeenAt,
            message.CreatedAt,
            message.EditedAt,
            message.DeletedAt
        );

        await _hub.Clients.Group($"conversation_{conversation.Id}").MessageCreated(dto);
        await _hub.Clients.Group($"user_{userId}").MessageCreated(dto);
        await _hub.Clients.Group($"user_{story.AuthorId}").MessageCreated(dto);

        return Ok(new { conversationId = conversation.Id, message = dto });
    }

    private async Task InvalidateStoriesCache(Guid userId)
    {
        await _cache.RemoveAsync($"stories_{userId}");

        var friendIds = await _db.Friendships
            .Where(f => f.UserId == userId && f.Status == "active")
            .Select(f => f.FriendId)
            .ToListAsync();

        foreach (var friendId in friendIds)
        {
            await _cache.RemoveAsync($"stories_{friendId}");
        }
    }
}

