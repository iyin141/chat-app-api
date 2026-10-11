using Microsoft.EntityFrameworkCore;
using ChatApp.Infrastructure.Data.Entities;

namespace ChatApp.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<UserMute> UserMutes => Set<UserMute>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Users
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Email).HasColumnName("email").HasColumnType("citext").IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(e => e.Profile)
                .WithOne(p => p.User)
                .HasForeignKey<Profile>(p => p.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Profiles
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username").HasColumnType("citext").IsRequired();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.DisplayName).HasColumnName("display_name").IsRequired();
            entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        // Conversations
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserLowId).HasColumnName("user_low_id").IsRequired(false);
            entity.Property(e => e.UserHighId).HasColumnName("user_high_id").IsRequired(false);
            entity.Property(e => e.LegacyConvoId).HasColumnName("legacy_convo_id");
            entity.Property(e => e.IsGroup).HasColumnName("is_group").HasDefaultValue(false);
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
            entity.HasIndex(e => e.LegacyConvoId).IsUnique();
            entity.Property(e => e.LastMessageId).HasColumnName("last_message_id");
            entity.Property(e => e.LastMessageAt).HasColumnName("last_message_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            entity.HasIndex(e => new { e.UserLowId, e.UserHighId })
                  .IsUnique()
                  .HasFilter("user_low_id IS NOT NULL AND user_high_id IS NOT NULL");

            entity.HasOne(e => e.UserLow)
                .WithMany()
                .HasForeignKey(e => e.UserLowId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasOne(e => e.UserHigh)
                .WithMany()
                .HasForeignKey(e => e.UserHighId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasOne(e => e.LastMessage)
                .WithMany()
                .HasForeignKey(e => e.LastMessageId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ConversationParticipants
        modelBuilder.Entity<ConversationParticipant>(entity =>
        {
            entity.ToTable("conversation_participants");
            entity.HasKey(e => new { e.ConversationId, e.UserId });
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Role).HasColumnName("role").HasDefaultValue("Member").IsRequired();
            entity.Property(e => e.JoinedAt).HasColumnName("joined_at").HasDefaultValueSql("now()");

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Participants)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserSettings
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.ToTable("user_settings");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.GlobalNotifications).HasColumnName("global_notifications").HasDefaultValue(true);
            entity.Property(e => e.PrivacyLevel).HasColumnName("privacy_level").HasDefaultValue("Anyone");
            entity.Property(e => e.Theme).HasColumnName("theme").HasDefaultValue("system");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserSettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserBlocks
        modelBuilder.Entity<UserBlock>(entity =>
        {
            entity.ToTable("user_blocks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.BlockerId).HasColumnName("blocker_id").IsRequired();
            entity.Property(e => e.BlockedId).HasColumnName("blocked_id").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            entity.HasIndex(e => new { e.BlockerId, e.BlockedId }).IsUnique();

            entity.HasOne(e => e.Blocker)
                .WithMany()
                .HasForeignKey(e => e.BlockerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Blocked)
                .WithMany()
                .HasForeignKey(e => e.BlockedId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // UserMutes
        modelBuilder.Entity<UserMute>(entity =>
        {
            entity.ToTable("user_mutes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired();
            entity.Property(e => e.MutedUntil).HasColumnName("muted_until");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            entity.HasIndex(e => new { e.UserId, e.ConversationId }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Friendships
        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.ToTable("friendships");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.FriendId).HasColumnName("friend_id").IsRequired();
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired();
            entity.Property(e => e.SavedName).HasColumnName("saved_name");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("active").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            entity.HasIndex(e => new { e.UserId, e.FriendId }).IsUnique();
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_friendships_user");

            entity.HasOne(e => e.User)
                .WithMany(p => p.Friendships)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Friend)
                .WithMany(p => p.FriendOf)
                .HasForeignKey(e => e.FriendId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Friendships)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // FriendRequests
        modelBuilder.Entity<FriendRequest>(entity =>
        {
            entity.ToTable("friend_requests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.SenderId).HasColumnName("sender_id").IsRequired();
            entity.Property(e => e.ReceiverId).HasColumnName("receiver_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(e => e.RespondedAt).HasColumnName("responded_at");

            entity.HasOne(e => e.Sender)
                .WithMany(p => p.SentRequests)
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Receiver)
                .WithMany(p => p.ReceivedRequests)
                .HasForeignKey(e => e.ReceiverId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Messages
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired();
            entity.Property(e => e.SenderId).HasColumnName("sender_id").IsRequired();
            entity.Property(e => e.ReceiverId).HasColumnName("receiver_id").IsRequired(false);
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.ReplyToId).HasColumnName("reply_to_id");
            entity.Property(e => e.TempId).HasColumnName("temp_id");
            entity.Property(e => e.Seen).HasColumnName("seen").HasDefaultValue(false).IsRequired();
            entity.Property(e => e.SeenAt).HasColumnName("seen_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(e => e.EditedAt).HasColumnName("edited_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");

            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_messages_conv_created");
            entity.HasIndex(e => new { e.ConversationId, e.TempId })
                .IsUnique()
                .HasDatabaseName("ux_messages_temp")
                .HasFilter("temp_id IS NOT NULL");
            entity.HasIndex(e => new { e.ReceiverId, e.ConversationId })
                .HasDatabaseName("ix_messages_unread")
                .HasFilter("seen = false AND deleted_at IS NULL");

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Sender)
                .WithMany(p => p.SentMessages)
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Receiver)
                .WithMany(p => p.ReceivedMessages)
                .HasForeignKey(e => e.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasOne(e => e.ReplyTo)
                .WithMany(m => m.Replies)
                .HasForeignKey(e => e.ReplyToId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Stories
        modelBuilder.Entity<Story>(entity =>
        {
            entity.ToTable("stories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.AuthorId).HasColumnName("author_id").IsRequired();
            entity.Property(e => e.MediaUrl).HasColumnName("media_url").IsRequired();
            entity.Property(e => e.MediaType).HasColumnName("media_type").IsRequired().HasDefaultValue("image");
            entity.Property(e => e.Caption).HasColumnName("caption");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();

            entity.HasIndex(e => e.ExpiresAt);

            entity.HasOne(e => e.Author)
                .WithMany(p => p.Stories)
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
