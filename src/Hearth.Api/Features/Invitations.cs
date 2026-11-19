using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Hearth.Api.Contracts;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Api.Features;

public static class Invitations
{
    public static void MapInvitations(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/servers/{server:guid}/invites").RequireAuthorization().WithTags("Invitations");
        g.MapPost("/", async (Guid server, InviteRequest r, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(server, p.UserId(), Permission.CreateInvites, ct);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var invite = new Invitation { ServerId = server, TokenHash = Hash(token), ExpiresAt = DateTimeOffset.UtcNow.AddHours(r.ExpiresInHours), MaxUses = r.MaxUses };
            db.Invitations.Add(invite); await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new { invite.Id, token, invite.ExpiresAt });
        });
        g.MapGet("/", async (Guid server, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(server, p.UserId(), Permission.CreateInvites, ct);
            return TypedResults.Ok(await db.Invitations.Where(x => x.ServerId == server).OrderByDescending(x => x.ExpiresAt).Take(100).Select(x => new { x.Id, x.ExpiresAt, x.MaxUses, x.Uses, x.Revoked }).ToArrayAsync(ct));
        });
        g.MapDelete("/{id:guid}", async (Guid server, Guid id, ClaimsPrincipal p, Access a, AppDbContext db, CancellationToken ct) =>
        {
            await a.Require(server, p.UserId(), Permission.CreateInvites, ct);
            var invite = Guard.Found(await db.Invitations.SingleOrDefaultAsync(x => x.Id == id && x.ServerId == server, ct));
            invite.Revoked = true; await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        api.MapPost("/invites/redeem", async (RedeemRequest r, ClaimsPrincipal p, AppDbContext db, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = Hash(r.Token);
            var invite = Guard.Found(await db.Invitations.FromSqlInterpolated($"SELECT * FROM \"Invitations\" WHERE \"TokenHash\" = {hash} FOR UPDATE").SingleOrDefaultAsync(ct));
            var user = p.UserId();
            Guard.Against(!Rules.CanInvite(invite, DateTimeOffset.UtcNow), "This invitation has expired, was revoked, or reached its usage limit.");
            Guard.Against(await db.Bans.AnyAsync(x => x.ServerId == invite.ServerId && x.UserId == user, ct), "You cannot join this server.", 403);
            if (!await db.Memberships.AnyAsync(x => x.ServerId == invite.ServerId && x.UserId == user, ct))
            {
                db.Memberships.Add(new() { ServerId = invite.ServerId, UserId = user }); invite.Uses++;
                db.Publish(serverId: invite.ServerId); db.Publish(userId: user);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct); return TypedResults.Ok(new { serverId = invite.ServerId });
        }).RequireAuthorization().RequireRateLimiting("auth").WithTags("Invitations");
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
