namespace Hearth.Domain;

[Flags]
public enum Permission { None = 0, ManageChannels = 1, CreateInvites = 2, ModerateMessages = 4, ModerateMembers = 8, All = 15 }
public enum RelationshipState { Pending, Accepted }

public sealed class Server
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class Membership
{
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ServerRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string Name { get; set; } = "";
    public Permission Permissions { get; set; }
    public bool IsDefault { get; set; }
}
public sealed class RoleAssignment
{
    public Guid ServerId { get; set; }
    public Guid RoleId { get; set; }
    public Guid UserId { get; set; }
}
public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ServerId { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public bool IsPrivate { get; set; }
    public Guid? UserA { get; set; }
    public Guid? UserB { get; set; }
}
public sealed class ChannelGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? UserId { get; set; }
}
public sealed class Message
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid AuthorId { get; set; }
    public Guid ClientId { get; set; }
    public string Content { get; set; } = "";
    public long? ReplyToId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; set; }
    public bool Deleted { get; set; }
}
public sealed class Reaction
{
    public long MessageId { get; set; }
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = "";
}
public sealed class ReadPosition
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public long MessageId { get; set; }
}
public sealed class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserA { get; set; }
    public Guid UserB { get; set; }
    public Guid RequestedBy { get; set; }
    public RelationshipState State { get; set; }
}
public sealed class Block { public Guid UserId { get; set; } public Guid TargetId { get; set; } }
public sealed class Ban { public Guid ServerId { get; set; } public Guid UserId { get; set; } }
public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int Uses { get; set; }
    public bool Revoked { get; set; }
}
public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UploaderId { get; set; }
    public long? MessageId { get; set; }
    public string FileName { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class OutboxEvent
{
    public long Id { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? ServerId { get; set; }
    public Guid? UserId { get; set; }
    public string Kind { get; set; } = "changed";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
}
public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid ActorId { get; set; }
    public Guid? TargetId { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class Rules
{
    public static (Guid A, Guid B) Pair(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
    public static bool CanModerate(bool owner, bool actorModerator, bool targetOwner, bool targetModerator)
        => !targetOwner && (owner || (actorModerator && !targetModerator));
    public static bool CanInvite(Invitation invite, DateTimeOffset now)
        => !invite.Revoked && invite.ExpiresAt > now && (invite.MaxUses is null || invite.Uses < invite.MaxUses);
    public static readonly string[] Emojis = ["👍", "❤️", "😂", "🎉", "👀", "🚀"];
}
