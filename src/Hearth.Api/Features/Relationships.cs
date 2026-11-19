using System.Security.Claims;
using Hearth.Api.Contracts;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Api.Features;

public static class Relationships
{
    public static void MapRelationships(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/relationships").RequireAuthorization().WithTags("Relationships");
        g.MapGet("/users", async (string? query, ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            if (query is null || query.Length < 3) return TypedResults.Ok(Array.Empty<UserDto>());
            var normalized = query.Trim().ToUpperInvariant(); var user = p.UserId();
            return TypedResults.Ok(await db.Users.Where(x => x.Id != user && x.NormalizedUserName!.StartsWith(normalized) && !db.Blocks.Any(b => (b.UserId == user && b.TargetId == x.Id) || (b.TargetId == user && b.UserId == x.Id))).Take(20).Select(x => new UserDto(x.Id, x.UserName!, x.DisplayName, x.AllowSharedServerDms)).ToArrayAsync(ct));
        });
        g.MapGet("/", async (ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            var user = p.UserId();
            var friendships = await db.Friendships.Where(x => x.UserA == user || x.UserB == user).ToArrayAsync(ct);
            var result = new List<RelationshipDto>();
            foreach (var f in friendships)
            {
                var other = f.UserA == user ? f.UserB : f.UserA; var u = Guard.Found(await db.Users.FindAsync([other], ct));
                result.Add(new(f.Id, u.Id, u.UserName!, u.DisplayName, f.State == RelationshipState.Accepted ? "accepted" : "pending", f.RequestedBy != user));
            }
            return TypedResults.Ok(result);
        });
        g.MapPost("/friends", async (TargetRequest r, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            var user = p.UserId(); Guard.Found(await db.Users.FindAsync([r.UserId], ct));
            Guard.Against(user == r.UserId || await a.Blocked(user, r.UserId, ct), "Friend request unavailable.", 403);
            var (first, second) = Rules.Pair(user, r.UserId);
            if (!await db.Friendships.AnyAsync(x => x.UserA == first && x.UserB == second, ct)) db.Friendships.Add(new() { UserA = first, UserB = second, RequestedBy = user });
            db.Publish(userId: user); db.Publish(userId: r.UserId); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapPost("/friends/{id:guid}/accept", async (Guid id, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            var f = Guard.Found(await db.Friendships.FindAsync([id], ct)); var user = p.UserId();
            Guard.Against((f.UserA != user && f.UserB != user) || f.RequestedBy == user || await a.Blocked(f.UserA, f.UserB, ct), "Friend request unavailable.", 403);
            f.State = RelationshipState.Accepted; db.Publish(userId: f.UserA); db.Publish(userId: f.UserB); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapDelete("/friends/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            var f = Guard.Found(await db.Friendships.FindAsync([id], ct));
            Guard.Against(f.UserA != p.UserId() && f.UserB != p.UserId(), "Resource not found.", 404);
            db.Friendships.Remove(f); db.Publish(userId: f.UserA); db.Publish(userId: f.UserB); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapGet("/blocks", async (ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            var user = p.UserId(); return TypedResults.Ok(await db.Users.Where(u => db.Blocks.Any(x => x.UserId == user && x.TargetId == u.Id)).Select(u => new UserDto(u.Id, u.UserName!, u.DisplayName, u.AllowSharedServerDms)).ToArrayAsync(ct));
        });
        g.MapPut("/blocks/{target:guid}", async (Guid target, ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            var user = p.UserId(); Guard.Against(user == target, "You cannot block yourself."); Guard.Found(await db.Users.FindAsync([target], ct));
            if (!await db.Blocks.AnyAsync(x => x.UserId == user && x.TargetId == target, ct)) db.Blocks.Add(new() { UserId = user, TargetId = target });
            var (a, b) = Rules.Pair(user, target); var f = await db.Friendships.SingleOrDefaultAsync(x => x.UserA == a && x.UserB == b, ct); if (f is not null) db.Friendships.Remove(f);
            db.Publish(userId: user); db.Publish(userId: target); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapDelete("/blocks/{target:guid}", async (Guid target, ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            var b = await db.Blocks.FindAsync([p.UserId(), target], ct); if (b is not null) db.Blocks.Remove(b);
            db.Publish(userId: p.UserId()); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        api.MapGet("/dms", async (ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            var user = p.UserId(); var conversations = await db.Conversations.Where(x => x.ServerId == null && (x.UserA == user || x.UserB == user)).ToArrayAsync(ct);
            var result = new List<ChannelDto>(); foreach (var c in conversations) result.Add(await Channels.Dto(c, user, db, ct)); return TypedResults.Ok(result);
        }).RequireAuthorization().WithTags("Messaging");
        api.MapPost("/dms", async (TargetRequest r, ClaimsPrincipal p, AppDbContext db, Access a, CancellationToken ct) =>
        {
            var user = p.UserId(); await a.CanDm(user, r.UserId, ct); var (first, second) = Rules.Pair(user, r.UserId);
            var c = await db.Conversations.SingleOrDefaultAsync(x => x.ServerId == null && x.UserA == first && x.UserB == second, ct);
            if (c is null) { c = new() { UserA = first, UserB = second }; db.Conversations.Add(c); db.Publish(userId: user); db.Publish(userId: r.UserId); await db.SaveChangesAsync(ct); }
            return TypedResults.Ok(await Channels.Dto(c, user, db, ct));
        }).RequireAuthorization().WithTags("Messaging");
    }
}
