using ChatApp.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Features.Realtime;

public interface IChatClient
{
    Task MessageCreated(object message);
    Task MessageUpdated(object message);
    Task MessageDeleted(long messageId, Guid conversationId);
    Task MessagesSeen(Guid conversationId, Guid seenByUserId);
    Task FriendRequestReceived(object request);
    Task FriendRequestUpdated(object request);
    Task FriendshipCreated(object friendship);
    Task StoryCreated(object story);
}

public class ChatHub : Hub<IChatClient>
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            }
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinUser(string userId)
    {
        var self = Context.UserIdentifier;
        if (string.IsNullOrEmpty(self) || !string.Equals(self, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException("Cannot join another user's inbox.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
    }

    public async Task JoinConversation(Guid conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
    }
}
