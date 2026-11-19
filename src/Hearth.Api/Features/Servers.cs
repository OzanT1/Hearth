using System.Security.Claims;
using Hearth.Api.Contracts;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Api.Features;

public static class Servers
{
    public static void MapServers(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/servers").RequireAuthorization().WithTags("Servers");
        g.MapGet("/", async (ClaimsPrincipal p, AppDbContext db, Access access, CancellationToken ct) =>
        {
            var user = p.UserId();
            var servers = await db.Servers.Where(s => db.Memberships.Any(m => m.ServerId == s.Id && m.UserId == user)).OrderBy(s => s.Name).ToListAsync(ct);
            var result = new List<ServerDto>();
            foreach (var s in servers) result.Add(new(s.Id, s.Name, s.OwnerId, await access.Permissions(s.Id, user, ct)));
            return TypedResults.Ok(result);
        });
        g.MapPost("/", async (NameRequest request, ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            var server = new Server { Name = request.Name.Trim(), OwnerId = p.UserId() };
            Guard.Against(server.Name.Length == 0, "Name is required.");
            db.Servers.Add(server);
            db.Memberships.Add(new() { ServerId = server.Id, UserId = p.UserId() });
            db.ServerRoles.Add(new() { ServerId = server.Id, Name = "Everyone", IsDefault = true, Permissions = Permission.CreateInvites });
            db.Conversations.Add(new() { ServerId = server.Id, Name = "general" });
            db.Publish(userId: p.UserId());
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/v1/servers/{server.Id}", new ServerDto(server.Id, server.Name, server.OwnerId, Permission.All));
        });
        g.MapPatch("/{id:guid}", async (Guid id, NameRequest r, ClaimsPrincipal p, Access access, AppDbContext db, CancellationToken ct) =>
        {
            var s = await access.Owner(id, p.UserId(), ct);
            s.Name = r.Name.Trim();
            Guard.Against(s.Name.Length == 0, "Name is required.");
            db.Publish(serverId: id); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal p, Access access, AppDbContext db, CancellationToken ct) =>
        {
            var s = await access.Owner(id, p.UserId(), ct);
            foreach (var user in await db.Memberships.Where(x => x.ServerId == id).Select(x => x.UserId).ToListAsync(ct)) db.Publish(userId: user);
            db.Servers.Remove(s); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapPost("/{id:guid}/transfer", async (Guid id, TargetRequest r, ClaimsPrincipal p, Access access, AppDbContext db, CancellationToken ct) =>
        {
            var s = await access.Owner(id, p.UserId(), ct); await access.Member(id, r.UserId, ct);
            s.OwnerId = r.UserId; Audit(db, id, p.UserId(), r.UserId, "ownership-transferred");
            db.Publish(serverId: id); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapPost("/{id:guid}/leave", async (Guid id, ClaimsPrincipal p, Access access, AppDbContext db, CancellationToken ct) =>
        {
            var s = await access.Member(id, p.UserId(), ct);
            Guard.Against(s.OwnerId == p.UserId(), "Transfer ownership before leaving.");
            db.Memberships.Remove(Guard.Found(await db.Memberships.FindAsync([id, p.UserId()], ct)));
            await RemoveMemberGrants(db, id, p.UserId(), ct);
            db.Publish(serverId: id); db.Publish(userId: p.UserId()); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapGet("/{id:guid}/members", async (Guid id, ClaimsPrincipal p, Access access, AppDbContext db, EphemeralState state, CancellationToken ct) =>
        {
            await access.Member(id, p.UserId(), ct);
            var users = await db.Users.Where(u => db.Memberships.Any(m => m.ServerId == id && m.UserId == u.Id)).OrderBy(u => u.DisplayName).ToListAsync(ct);
            var assignments = await db.RoleAssignments.Where(x => x.ServerId == id).ToListAsync(ct);
            var result = new List<MemberDto>();
            foreach (var u in users)
            {
                bool online;
                try { online = await state.Online(u.Id); } catch (StackExchange.Redis.RedisException) { online = false; }
                result.Add(new(u.Id, u.UserName!, u.DisplayName, assignments.Where(x => x.UserId == u.Id).Select(x => x.RoleId).ToArray(), online));
            }
            return TypedResults.Ok(result);
        });
        g.MapPost("/{id:guid}/members/{user:guid}/{action}", async (Guid id, Guid user, string action, ClaimsPrincipal p, Access access, AppDbContext db, CancellationToken ct) =>
        {
            Guard.Against(action is not ("kick" or "ban"), "Invalid moderation action.");
            var server = await access.Member(id, p.UserId(), ct);
            await access.Member(id, user, ct);
            var actor = await access.Permissions(id, p.UserId(), ct);
            var target = await access.Permissions(id, user, ct);
            Guard.Against(!Rules.CanModerate(server.OwnerId == p.UserId(), actor.HasFlag(Permission.ModerateMembers), server.OwnerId == user, target.HasFlag(Permission.ModerateMembers)), "You cannot moderate this member.", 403);
            db.Memberships.Remove(Guard.Found(await db.Memberships.FindAsync([id, user], ct)));
            await RemoveMemberGrants(db, id, user, ct);
            if (action == "ban") db.Bans.Add(new() { ServerId = id, UserId = user });
            Audit(db, id, p.UserId(), user, action);
            db.Publish(serverId: id); db.Publish(userId: user); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapGet("/{id:guid}/bans", async (Guid id, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(id, p.UserId(), Permission.ModerateMembers, ct);
            return TypedResults.Ok(await db.Users.Where(u => db.Bans.Any(b => b.ServerId == id && b.UserId == u.Id)).Select(u => new UserDto(u.Id, u.UserName!, u.DisplayName, u.AllowSharedServerDms)).ToListAsync(ct));
        });
        g.MapDelete("/{id:guid}/bans/{user:guid}", async (Guid id, Guid user, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(id, p.UserId(), Permission.ModerateMembers, ct);
            var ban = await db.Bans.FindAsync([id, user], ct); if (ban is not null) db.Bans.Remove(ban);
            Audit(db, id, p.UserId(), user, "unban"); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapGet("/{id:guid}/audit", async (Guid id, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Owner(id, p.UserId(), ct);
            return TypedResults.Ok(await db.Audit.Where(x => x.ServerId == id).OrderByDescending(x => x.Id).Take(100).Select(x => new { x.Id, x.ActorId, x.TargetId, x.Action, x.CreatedAt }).ToArrayAsync(ct));
        });
    }
    private static async Task RemoveMemberGrants(AppDbContext db, Guid server, Guid user, CancellationToken ct)
    {
        var channels = db.Conversations.Where(x => x.ServerId == server).Select(x => x.Id);
        db.ChannelGrants.RemoveRange(await db.ChannelGrants.Where(x => x.UserId == user && channels.Contains(x.ConversationId)).ToListAsync(ct));
    }
    public static void Audit(AppDbContext db, Guid server, Guid actor, Guid? target, string action)
        => db.Audit.Add(new() { ServerId = server, ActorId = actor, TargetId = target, Action = action });
}
