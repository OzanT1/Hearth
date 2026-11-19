using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Api;

public sealed class Access(AppDbContext db)
{
    public async Task<Server> Member(Guid server, Guid user, CancellationToken ct)
    {
        Guard.Against(!await db.Memberships.AnyAsync(x => x.ServerId == server && x.UserId == user, ct), "Resource not found.", 404);
        return Guard.Found(await db.Servers.FindAsync([server], ct));
    }
    public async Task<Permission> Permissions(Guid server, Guid user, CancellationToken ct)
    {
        var entity = await Member(server, user, ct);
        if (entity.OwnerId == user) return Permission.All;
        var assigned = db.RoleAssignments.Where(x => x.ServerId == server && x.UserId == user).Select(x => x.RoleId);
        var values = await db.ServerRoles.Where(x => x.ServerId == server && (x.IsDefault || assigned.Contains(x.Id))).Select(x => x.Permissions).ToListAsync(ct);
        return values.Aggregate(Permission.None, (a, b) => a | b);
    }
    public async Task Require(Guid server, Guid user, Permission permission, CancellationToken ct)
        => Guard.Against(((await Permissions(server, user, ct)) & permission) != permission, "You do not have permission.", 403);
    public async Task<Server> Owner(Guid server, Guid user, CancellationToken ct)
    {
        var entity = await Member(server, user, ct);
        Guard.Against(entity.OwnerId != user, "Only the server owner can do that.", 403);
        return entity;
    }
    public async Task<bool> CanRead(Conversation c, Guid user, CancellationToken ct)
    {
        if (c.ServerId is not Guid server) return c.UserA == user || c.UserB == user;
        if (!await db.Memberships.AnyAsync(x => x.ServerId == server && x.UserId == user, ct)) return false;
        if (!c.IsPrivate || await db.Servers.AnyAsync(x => x.Id == server && x.OwnerId == user, ct)) return true;
        var roles = db.ServerRoles.Where(x => x.ServerId == server && (x.IsDefault || db.RoleAssignments.Any(a => a.RoleId == x.Id && a.UserId == user))).Select(x => x.Id);
        return await db.ChannelGrants.AnyAsync(x => x.ConversationId == c.Id && (x.UserId == user || (x.RoleId != null && roles.Contains(x.RoleId.Value))), ct);
    }
    public async Task<Conversation> Read(Guid id, Guid user, CancellationToken ct)
    {
        var c = Guard.Found(await db.Conversations.FindAsync([id], ct));
        Guard.Against(!await CanRead(c, user, ct), "Resource not found.", 404);
        return c;
    }
    public Task<bool> Blocked(Guid a, Guid b, CancellationToken ct) => db.Blocks.AnyAsync(x => (x.UserId == a && x.TargetId == b) || (x.UserId == b && x.TargetId == a), ct);
    public async Task CanDm(Guid a, Guid b, CancellationToken ct)
    {
        Guard.Against(a == b || await Blocked(a, b, ct), "Direct messaging is unavailable.", 403);
        var (first, second) = Rules.Pair(a, b);
        if (await db.Friendships.AnyAsync(x => x.UserA == first && x.UserB == second && x.State == RelationshipState.Accepted, ct)) return;
        var bothAllow = await db.Users.CountAsync(x => (x.Id == a || x.Id == b) && x.AllowSharedServerDms, ct) == 2;
        var servers = db.Memberships.Where(x => x.UserId == a).Select(x => x.ServerId);
        Guard.Against(!bothAllow || !await db.Memberships.AnyAsync(x => x.UserId == b && servers.Contains(x.ServerId), ct), "Direct messaging is unavailable. Add this user as a friend.", 403);
    }
    public async Task<Conversation> Send(Guid id, Guid user, CancellationToken ct)
    {
        var c = await Read(id, user, ct);
        if (c.ServerId is null) await CanDm(user, c.UserA == user ? c.UserB!.Value : c.UserA!.Value, ct);
        return c;
    }
    public async Task<Guid[]> Readers(Conversation c, CancellationToken ct)
    {
        if (c.ServerId is null) return [c.UserA!.Value, c.UserB!.Value];
        var members = await db.Memberships.Where(x => x.ServerId == c.ServerId).Select(x => x.UserId).ToArrayAsync(ct);
        var allowed = new List<Guid>();
        foreach (var user in members) if (await CanRead(c, user, ct)) allowed.Add(user);
        return allowed.ToArray();
    }
}
