using Hearth.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Infrastructure;

public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool AllowSharedServerDms { get; set; } = true;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<ServerRole> ServerRoles => Set<ServerRole>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChannelGrant> ChannelGrants => Set<ChannelGrant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<ReadPosition> ReadPositions => Set<ReadPosition>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Ban> Bans => Set<Ban>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        ConfigureDomain(b);
    }

    public static void ConfigureDomain(ModelBuilder b)
    {
        b.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(80);
        b.Entity<AppUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<Server>().Property(x => x.Name).HasMaxLength(80);
        b.Entity<Server>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Membership>().HasKey(x => new { x.ServerId, x.UserId });
        b.Entity<Membership>().HasIndex(x => x.UserId);
        b.Entity<Membership>().HasOne<Server>().WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Membership>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ServerRole>().Property(x => x.Name).HasMaxLength(40);
        b.Entity<ServerRole>().HasOne<Server>().WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ServerRole>().HasIndex(x => x.ServerId).IsUnique().HasFilter("\"IsDefault\" = true");
        b.Entity<ServerRole>().HasAlternateKey(x => new { x.ServerId, x.Id });
        b.Entity<RoleAssignment>().HasKey(x => new { x.ServerId, x.RoleId, x.UserId });
        b.Entity<RoleAssignment>().HasOne<ServerRole>().WithMany().HasForeignKey(x => new { x.ServerId, x.RoleId }).HasPrincipalKey(x => new { x.ServerId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RoleAssignment>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.ServerId, x.UserId }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Conversation>().Property(x => x.Name).HasMaxLength(80);
        b.Entity<Conversation>().HasOne<Server>().WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Conversation>().HasIndex(x => new { x.UserA, x.UserB }).IsUnique().HasFilter("\"ServerId\" IS NULL");
        b.Entity<Conversation>().ToTable(t => t.HasCheckConstraint("CK_Conversation_Kind", "(\"ServerId\" IS NOT NULL AND \"UserA\" IS NULL AND \"UserB\" IS NULL) OR (\"ServerId\" IS NULL AND \"UserA\" IS NOT NULL AND \"UserB\" IS NOT NULL AND \"UserA\" <> \"UserB\")"));
        b.Entity<ChannelGrant>().HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChannelGrant>().HasOne<ServerRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChannelGrant>().ToTable(t => t.HasCheckConstraint("CK_Grant_Target", "(\"RoleId\" IS NULL) <> (\"UserId\" IS NULL)"));
        b.Entity<Message>().Property(x => x.Content).HasMaxLength(4000);
        b.Entity<Message>().HasIndex(x => new { x.ConversationId, x.Id });
        b.Entity<Message>().HasIndex(x => new { x.AuthorId, x.ClientId }).IsUnique();
        b.Entity<Message>().HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Message>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Reaction>().HasKey(x => new { x.MessageId, x.UserId, x.Emoji });
        b.Entity<Reaction>().Property(x => x.Emoji).HasMaxLength(16);
        b.Entity<Reaction>().HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ReadPosition>().HasKey(x => new { x.ConversationId, x.UserId });
        b.Entity<ReadPosition>().HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Friendship>().HasIndex(x => new { x.UserA, x.UserB }).IsUnique();
        b.Entity<Block>().HasKey(x => new { x.UserId, x.TargetId });
        b.Entity<Ban>().HasKey(x => new { x.ServerId, x.UserId });
        b.Entity<Ban>().HasOne<Server>().WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Invitation>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<Invitation>().Property(x => x.TokenHash).HasMaxLength(64);
        b.Entity<Invitation>().HasOne<Server>().WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Attachment>().HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Attachment>().HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Attachment>().HasIndex(x => x.CreatedAt);
        b.Entity<OutboxEvent>().HasIndex(x => x.CreatedAt);
        b.Entity<AuditEvent>().HasIndex(x => new { x.ServerId, x.Id });
    }

    public void Publish(Guid? conversationId = null, Guid? serverId = null, Guid? userId = null, string kind = "changed")
        => Outbox.Add(new OutboxEvent { ConversationId = conversationId, ServerId = serverId, UserId = userId, Kind = kind });
}
