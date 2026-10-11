using System.Security.Claims;
using ChatApp.Api.Features.Friends;
using ChatApp.Api.Features.Friends.Dtos;
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
using Testcontainers.PostgreSql;

namespace ChatApp.Tests.Integration;

public class PostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _db = new AppDbContext(options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
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

    private async Task<(Profile alice, Profile bob, Conversation convo)> SeedPairAsync()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        var aliceUser = new User { Id = aliceId, Email = $"alice_{aliceId:N}@example.com", PasswordHash = "hash" };
        var bobUser = new User { Id = bobId, Email = $"bob_{bobId:N}@example.com", PasswordHash = "hash" };

        var alice = new Profile { Id = aliceId, Username = $"alice_{aliceId:N}"[..20], DisplayName = "Alice" };
        var bob = new Profile { Id = bobId, Username = $"bob_{bobId:N}"[..20], DisplayName = "Bob" };
        var convo = new Conversation
        {
            Id = Guid.NewGuid(),
            UserLowId = alice.Id.CompareTo(bob.Id) < 0 ? alice.Id : bob.Id,
            UserHighId = alice.Id.CompareTo(bob.Id) < 0 ? bob.Id : alice.Id
        };
        _db.Users.AddRange(aliceUser, bobUser);
        _db.Profiles.AddRange(alice, bob);
        _db.Conversations.Add(convo);
        await _db.SaveChangesAsync();
        return (alice, bob, convo);
    }

    [Fact]
    public async Task GetMessages_Pagination_ReturnsChronologicalPages()
    {
        var (alice, bob, convo) = await SeedPairAsync();

        for (var i = 1; i <= 5; i++)
        {
            _db.Messages.Add(new Message
            {
                ConversationId = convo.Id,
                SenderId = alice.Id,
                ReceiverId = bob.Id,
                Content = $"msg {i}",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(i)
            });
        }
        await _db.SaveChangesAsync();

        var controller = new MessagesController(_db, new NullHubContext());
        SetUser(controller, bob.Id);

        // Latest 2 (ids 5, 4) returned chronologically
        var page1 = await controller.GetMessages(convo.Id, null, 2);
        var page1Dto = page1.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<List<MessageDto>>().Subject;
        page1Dto.Select(m => m.Content).Should().Equal("msg 4", "msg 5");

        // Next page via `before` cursor
        var page2 = await controller.GetMessages(convo.Id, page1Dto[0].Id, 2);
        var page2Dto = page2.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<List<MessageDto>>().Subject;
        page2Dto.Select(m => m.Content).Should().Equal("msg 2", "msg 3");
    }

    [Fact]
    public async Task MarkAsRead_ThenUnreadCount_DropsToZero()
    {
        var (alice, bob, convo) = await SeedPairAsync();

        _db.Messages.Add(new Message
        {
            ConversationId = convo.Id,
            SenderId = alice.Id,
            ReceiverId = bob.Id,
            Content = "unread hello",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var controller = new MessagesController(_db, new NullHubContext());
        SetUser(controller, bob.Id);

        var before = await controller.GetTotalUnreadCount();
        before.Result.Should().BeOfType<OkObjectResult>();

        await controller.MarkAsRead(convo.Id, new MarkReadRequest(null, null, null));

        var after = await controller.GetTotalUnreadCount();
        var okAfter = after.Result.Should().BeOfType<OkObjectResult>().Subject;
        okAfter.Value.Should().BeEquivalentTo(new { totalUnread = 0 });
    }

    [Fact]
    public async Task SendMessage_DoubleSubmitTempId_OnRealPostgres_PersistsSingleRow()
    {
        var (alice, bob, convo) = await SeedPairAsync();

        var controller = new MessagesController(_db, new NullHubContext());
        SetUser(controller, alice.Id);

        var first = await controller.SendMessage(convo.Id, new SendMessageRequest("hi", "temp-pg", null));
        var second = await controller.SendMessage(convo.Id, new SendMessageRequest("hi", "temp-pg", null));

        var firstDto = first.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MessageDto>().Subject;
        var secondDto = second.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MessageDto>().Subject;
        secondDto.Id.Should().Be(firstDto.Id);

        // Unique filtered index (conversation_id, temp_id) means a raw duplicate insert would throw;
        // the idempotency lookup prevents that path from ever executing.
        var count = await _db.Messages.CountAsync(m => m.TempId == "temp-pg");
        count.Should().Be(1);
    }

    [Fact]
    public async Task AcceptFriendRequest_Twice_SecondCallReturnsBadRequest_AndSingleFriendshipPair()
    {
        var (alice, bob, _) = await SeedPairAsync();

        var requestsController = new FriendRequestsController(_db, new NullHubContext());
        SetUser(requestsController, alice.Id);

        var created = await requestsController.SendFriendRequest(new SendFriendRequest(bob.Id));
        var dto = created.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FriendRequestDto>().Subject;

        SetUser(requestsController, bob.Id);
        var firstAccept = await requestsController.AcceptFriendRequest(dto.Id);
        firstAccept.Should().BeOfType<OkObjectResult>();

        var secondAccept = await requestsController.AcceptFriendRequest(dto.Id);
        secondAccept.Should().BeOfType<BadRequestObjectResult>();

        var friendshipCount = await _db.Friendships.CountAsync(f =>
            (f.UserId == alice.Id && f.FriendId == bob.Id) ||
            (f.UserId == bob.Id && f.FriendId == alice.Id));
        friendshipCount.Should().Be(2); // exactly one reciprocal pair, no duplicates

        var conversationCount = await _db.Conversations.CountAsync(c =>
            (c.UserLowId == alice.Id && c.UserHighId == bob.Id) ||
            (c.UserLowId == bob.Id && c.UserHighId == alice.Id));
        conversationCount.Should().Be(1);
    }
}
