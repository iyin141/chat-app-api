using System.Security.Claims;
using ChatApp.Api.Features.Messaging;
using ChatApp.Api.Features.Messaging.Dtos;
using ChatApp.Api.Features.Realtime;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChatApp.Tests.Authorization;

public class AuthorizationAndParityTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(ControllerBase controller, Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private class NullHubContext : IHubContext<ChatHub, IChatClient>
    {
        public IHubClients<IChatClient> Clients => new NullHubClients();
        public IGroupManager Groups => throw new NotImplementedException();
    }

    private class NullHubClients : IHubClients<IChatClient>
    {
        public IChatClient All => new NullChatClient();
        public IChatClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => new NullChatClient();
        public IChatClient Client(string connectionId) => new NullChatClient();
        public IChatClient Clients(IReadOnlyList<string> connectionIds) => new NullChatClient();
        public IChatClient Group(string groupName) => new NullChatClient();
        public IChatClient Groups(IReadOnlyList<string> groupNames) => new NullChatClient();
        public IChatClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new NullChatClient();
        public IChatClient User(string userId) => new NullChatClient();
        public IChatClient Users(IReadOnlyList<string> userIds) => new NullChatClient();
    }

    private class NullChatClient : IChatClient
    {
        public Task MessageCreated(object message) => Task.CompletedTask;
        public Task MessageUpdated(object message) => Task.CompletedTask;
        public Task MessageDeleted(long messageId, Guid conversationId) => Task.CompletedTask;
        public Task MessagesSeen(Guid conversationId, Guid seenByUserId) => Task.CompletedTask;
        public Task FriendRequestReceived(object request) => Task.CompletedTask;
        public Task FriendRequestUpdated(object request) => Task.CompletedTask;
        public Task FriendshipCreated(object friendship) => Task.CompletedTask;
        public Task StoryCreated(object story) => Task.CompletedTask;
    }

    [Fact]
    public async Task GetMessages_WhenUserNotInConversation_ReturnsForbid()
    {
        var db = CreateInMemoryDb();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var intruder = Guid.NewGuid();

        var convo = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = user1,
            UserHighId = user2
        };
        db.Conversations.Add(convo);
        await db.SaveChangesAsync();

        var controller = new MessagesController(db, new NullHubContext());
        SetUser(controller, intruder);

        var result = await controller.GetMessages(convo.Id, null, 50);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DeleteMessage_WhenUserIsNotSender_ReturnsForbid()
    {
        var db = CreateInMemoryDb();
        var sender = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        var convo = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = sender,
            UserHighId = otherUser
        };
        db.Conversations.Add(convo);

        var message = new Message
        {
            Id = 1,
            ConversationId = convo.Id,
            SenderId = sender,
            Content = "Test"
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var controller = new MessagesController(db, new NullHubContext());
        SetUser(controller, otherUser); // Attempting to delete someone else's message

        var result = await controller.DeleteMessage(message.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task SendMessage_ReconcilesTempId_AndPersistsSuccessfully()
    {
        var db = CreateInMemoryDb();
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        var convo = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = sender < recipient ? sender : recipient,
            UserHighId = sender < recipient ? recipient : sender
        };
        db.Conversations.Add(convo);
        await db.SaveChangesAsync();

        var controller = new MessagesController(db, new NullHubContext());
        SetUser(controller, sender);

        var response = await controller.SendMessage(convo.Id, new SendMessageRequest("Hello world!", "temp-12345", null));

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<MessageDto>().Subject;

        dto.TempId.Should().Be("temp-12345");
        dto.Content.Should().Be("Hello world!");
        dto.SenderId.Should().Be(sender);

        var persisted = await db.Messages.FindAsync(dto.Id);
        persisted.Should().NotBeNull();
        persisted!.Content.Should().Be("Hello world!");
    }

    [Fact]
    public async Task SendMessage_DoubleSubmitSameTempId_PersistsOnlyOneMessage()
    {
        var db = CreateInMemoryDb();
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        var convo = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = sender < recipient ? sender : recipient,
            UserHighId = sender < recipient ? recipient : sender
        };
        db.Conversations.Add(convo);
        await db.SaveChangesAsync();

        var controller = new MessagesController(db, new NullHubContext());
        SetUser(controller, sender);

        var first = await controller.SendMessage(convo.Id, new SendMessageRequest("Hello world!", "temp-dup", null));
        var second = await controller.SendMessage(convo.Id, new SendMessageRequest("Hello world!", "temp-dup", null));

        var firstDto = first.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MessageDto>().Subject;
        var secondDto = second.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MessageDto>().Subject;

        secondDto.Id.Should().Be(firstDto.Id);

        var count = await db.Messages.CountAsync(m => m.ConversationId == convo.Id && m.TempId == "temp-dup");
        count.Should().Be(1);
    }

    [Fact]
    public async Task SendMessage_WithReplyToIdFromOtherConversation_ReturnsBadRequest()
    {
        var db = CreateInMemoryDb();
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        var convoA = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = sender < recipient ? sender : recipient,
            UserHighId = sender < recipient ? recipient : sender
        };
        var otherUser = Guid.NewGuid();
        var convoB = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = sender < otherUser ? sender : otherUser,
            UserHighId = sender < otherUser ? otherUser : sender
        };
        db.Conversations.AddRange(convoA, convoB);

        var foreignMessage = new Message
        {
            Id = 99,
            ConversationId = convoB.Id,
            SenderId = otherUser,
            ReceiverId = sender,
            Content = "foreign"
        };
        db.Messages.Add(foreignMessage);
        await db.SaveChangesAsync();

        var controller = new MessagesController(db, new NullHubContext());
        SetUser(controller, sender);

        var response = await controller.SendMessage(convoA.Id, new SendMessageRequest("reply attempt", null, 99));

        response.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task MarkAsRead_WhenUserNotInConversation_ReturnsForbid()
    {
        var db = CreateInMemoryDb();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var intruder = Guid.NewGuid();

        var convo = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = user1,
            UserHighId = user2
        };
        db.Conversations.Add(convo);

        var message = new Message
        {
            Id = 1,
            ConversationId = convo.Id,
            SenderId = user1,
            ReceiverId = user2,
            Content = "unread"
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var controller = new MessagesController(db, new NullHubContext());
        SetUser(controller, intruder);

        var result = await controller.MarkAsRead(convo.Id, new MarkReadRequest(null, null, null));

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task MarkAsRead_ByReceiver_MarksOnlyIncomingMessages()
    {
        var db = CreateInMemoryDb();
        var sender = Guid.NewGuid();
        var receiver = Guid.NewGuid();

        var convo = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = sender < receiver ? sender : receiver,
            UserHighId = sender < receiver ? receiver : sender
        };
        db.Conversations.Add(convo);

        db.Messages.Add(new Message
        {
            Id = 1,
            ConversationId = convo.Id,
            SenderId = sender,
            ReceiverId = receiver,
            Content = "to receiver"
        });
        db.Messages.Add(new Message
        {
            Id = 2,
            ConversationId = convo.Id,
            SenderId = receiver,
            ReceiverId = sender,
            Content = "to sender"
        });
        await db.SaveChangesAsync();

        var controller = new MessagesController(db, new NullHubContext());
        SetUser(controller, receiver);

        var result = await controller.MarkAsRead(convo.Id, new MarkReadRequest(null, null, null));

        result.Should().BeOfType<OkObjectResult>();

        var msg1 = await db.Messages.FindAsync(1L);
        var msg2 = await db.Messages.FindAsync(2L);
        msg1!.Seen.Should().BeTrue();
        msg1.SeenAt.Should().NotBeNull();
        msg2!.Seen.Should().BeFalse();
    }
}
