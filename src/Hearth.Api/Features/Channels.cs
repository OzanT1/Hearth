using System.Security.Claims;
using Hearth.Api.Contracts;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Api.Features;

public static class Channels
{
    public static void MapChannels(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/servers/{server:guid}").RequireAuthorization().WithTags("Channels and roles");
        g.MapGet("/channels", async (Guid server, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Member(server, p.UserId(), ct);
            var channels = await db.Conversations.Where(x => x.ServerId == server).OrderBy(x => x.Position).ThenBy(x => x.Name).ToListAsync(ct);
            var result = new List<ChannelDto>();
            foreach (var c in channels) if (await a.CanRead(c, p.UserId(), ct)) result.Add(await Dto(c, p.UserId(), db, ct));
            return TypedResults.Ok(result);
        });
        g.MapPost("/channels", async (Guid server, ChannelRequest r, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(server, p.UserId(), Permission.ManageChannels, ct);
            var c = new Conversation { ServerId = server }; db.Conversations.Add(c);
            await Configure(c, r, db, ct);
            db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.Ok(await Dto(c, p.UserId(), db, ct));
        });
        g.MapPut("/channels/{id:guid}", async (Guid server, Guid id, ChannelRequest r, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(server, p.UserId(), Permission.ManageChannels, ct);
            var c = await a.Read(id, p.UserId(), ct); Guard.Against(c.ServerId != server, "Resource not found.", 404);
            await Configure(c, r, db, ct); db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapDelete("/channels/{id:guid}", async (Guid server, Guid id, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(server, p.UserId(), Permission.ManageChannels, ct);
            var c = await a.Read(id, p.UserId(), ct); Guard.Against(c.ServerId != server, "Resource not found.", 404);
            db.Conversations.Remove(c); db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapGet("/roles", async (Guid server, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Member(server, p.UserId(), ct);
            return TypedResults.Ok(await db.ServerRoles.Where(x => x.ServerId == server).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).Select(x => new RoleDto(x.Id, x.Name, x.Permissions, x.IsDefault)).ToArrayAsync(ct));
        });
        g.MapPost("/roles", async (Guid server, RoleRequest r, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Owner(server, p.UserId(), ct); Validate(r);
            var role = new ServerRole { ServerId = server, Name = r.Name.Trim(), Permissions = r.Permissions }; db.ServerRoles.Add(role);
            db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.Ok(new RoleDto(role.Id, role.Name, role.Permissions, false));
        });
        g.MapPut("/roles/{id:guid}", async (Guid server, Guid id, RoleRequest r, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Owner(server, p.UserId(), ct); Validate(r);
            var role = Guard.Found(await db.ServerRoles.SingleOrDefaultAsync(x => x.Id == id && x.ServerId == server, ct));
            role.Name = r.Name.Trim(); role.Permissions = r.Permissions;
            db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapDelete("/roles/{id:guid}", async (Guid server, Guid id, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Owner(server, p.UserId(), ct);
            var role = Guard.Found(await db.ServerRoles.SingleOrDefaultAsync(x => x.Id == id && x.ServerId == server, ct));
            Guard.Against(role.IsDefault, "The default role cannot be deleted."); db.ServerRoles.Remove(role);
            db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapPut("/roles/{role:guid}/members/{user:guid}", async (Guid server, Guid role, Guid user, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Owner(server, p.UserId(), ct); await a.Member(server, user, ct);
            Guard.Found(await db.ServerRoles.SingleOrDefaultAsync(x => x.Id == role && x.ServerId == server && !x.IsDefault, ct));
            if (!await db.RoleAssignments.AnyAsync(x => x.ServerId == server && x.RoleId == role && x.UserId == user, ct)) db.RoleAssignments.Add(new() { ServerId = server, RoleId = role, UserId = user });
            db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapDelete("/roles/{role:guid}/members/{user:guid}", async (Guid server, Guid role, Guid user, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Owner(server, p.UserId(), ct);
            var entry = await db.RoleAssignments.FindAsync([server, role, user], ct); if (entry is not null) db.RoleAssignments.Remove(entry);
            db.Publish(serverId: server); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
    }
    private static void Validate(RoleRequest r) => Guard.Against((r.Permissions & ~Permission.All) != 0 || r.Name.Trim().Length == 0, "Invalid role.");
    private static async Task Configure(Conversation c, ChannelRequest r, AppDbContext db, CancellationToken ct)
    {
        Guard.Against(r.RoleIds is null || r.UserIds is null, "Allowlists are required.");
        var roles = r.RoleIds!.Distinct().ToArray(); var users = r.UserIds!.Distinct().ToArray();
        Guard.Against(await db.ServerRoles.CountAsync(x => x.ServerId == c.ServerId && roles.Contains(x.Id), ct) != roles.Length || await db.Memberships.CountAsync(x => x.ServerId == c.ServerId && users.Contains(x.UserId), ct) != users.Length, "All allowlist entries must belong to this server.");
        c.Name = r.Name; c.Position = r.Position; c.IsPrivate = r.IsPrivate;
        db.ChannelGrants.RemoveRange(await db.ChannelGrants.Where(x => x.ConversationId == c.Id).ToListAsync(ct));
        if (!c.IsPrivate) return;
        foreach (var role in roles) db.ChannelGrants.Add(new() { ConversationId = c.Id, RoleId = role });
        foreach (var user in users) db.ChannelGrants.Add(new() { ConversationId = c.Id, UserId = user });
    }
    public static async Task<ChannelDto> Dto(Conversation c, Guid user, AppDbContext db, CancellationToken ct)
    {
        var last = await db.ReadPositions.Where(x => x.ConversationId == c.Id && x.UserId == user).Select(x => x.MessageId).FirstOrDefaultAsync(ct);
        var count = await db.Messages.LongCountAsync(x => x.ConversationId == c.Id && x.Id > last && x.AuthorId != user && !x.Deleted, ct);
        var grants = await db.ChannelGrants.Where(x => x.ConversationId == c.Id).ToArrayAsync(ct);
        var other = c.ServerId is null ? (c.UserA == user ? c.UserB : c.UserA) : null;
        var name = other.HasValue ? await db.Users.Where(x => x.Id == other).Select(x => x.DisplayName).SingleAsync(ct) : c.Name;
        return new(c.Id, c.ServerId, name, c.Position, c.IsPrivate, other, count, grants.Where(x => x.RoleId.HasValue).Select(x => x.RoleId!.Value).ToArray(), grants.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).ToArray());
    }
}
