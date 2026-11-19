using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Hearth.Api.Realtime;

public sealed class Connections
{
    private readonly ConcurrentDictionary<string, (Guid User, HubCallerContext Context, TokenBucketRateLimiter Limiter)> entries = new();
    public bool Add(Guid user, HubCallerContext context)
    {
        lock (entries)
        {
            if (entries.Count >= 50 || entries.Values.Count(x => x.User == user) >= 5) return false;
            entries[context.ConnectionId] = (user, context, new(new()
            {
                TokenLimit = 10, TokensPerPeriod = 2, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true, QueueLimit = 0
            }));
            return true;
        }
    }
    public bool AllowInvocation(string id)
    {
        lock (entries)
        {
            if (!entries.TryGetValue(id, out var connection)) return false;
            using var lease = connection.Limiter.AttemptAcquire();
            return lease.IsAcquired;
        }
    }
    public void Remove(string id)
    {
        lock (entries) if (entries.TryRemove(id, out var entry)) entry.Limiter.Dispose();
    }
    public void Revoke(Guid user)
    {
        foreach (var e in entries.Values.Where(x => x.User == user)) e.Context.Abort();
    }
}

[Authorize]
public sealed class ChatHub(Access access, AppDbContext db, EphemeralState state, Connections connections, MutationGate gate, SignInManager<AppUser> signIn) : Hub
{
    private Guid User => Context.User!.UserId();
    public override async Task OnConnectedAsync()
    {
        if (!await gate.Semaphore.WaitAsync(TimeSpan.FromSeconds(2), Context.ConnectionAborted)) { Context.Abort(); return; }
        try
        {
            if (await signIn.ValidateSecurityStampAsync(Context.User!) is null) { Context.Abort(); return; }
            if (!connections.Add(User, Context)) { Context.Abort(); return; }
            try { await state.Heartbeat(User, Context.ConnectionId); } catch (RedisException) { }
            await Presence();
        }
        finally { gate.Semaphore.Release(); }
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        try { await state.Disconnect(User, Context.ConnectionId); } catch (RedisException) { }
        await base.OnDisconnectedAsync(exception);
    }
    public async Task Heartbeat()
    {
        if (!connections.AllowInvocation(Context.ConnectionId)) { Context.Abort(); return; }
        if (!await gate.Semaphore.WaitAsync(TimeSpan.FromSeconds(2), Context.ConnectionAborted)) throw new HubException("Server busy. Please retry.");
        try
        {
            if (await signIn.ValidateSecurityStampAsync(Context.User!) is null) { Context.Abort(); return; }
            try { await state.Heartbeat(User, Context.ConnectionId); } catch (RedisException) { }
        }
        finally { gate.Semaphore.Release(); }
    }
    public async Task Typing(Guid conversationId)
    {
        if (!connections.AllowInvocation(Context.ConnectionId)) { Context.Abort(); return; }
        if (!await gate.Semaphore.WaitAsync(TimeSpan.FromSeconds(2), Context.ConnectionAborted)) throw new HubException("Server busy. Please retry.");
        try
        {
            if (await signIn.ValidateSecurityStampAsync(Context.User!) is null) { Context.Abort(); return; }
            var c = await access.Send(conversationId, User, Context.ConnectionAborted);
            try { if (!await state.Typing(conversationId, User)) return; } catch (RedisException) { return; }
            var recipients = new List<string>();
            foreach (var u in await access.Readers(c, Context.ConnectionAborted))
                if (u != User && !await access.Blocked(User, u, Context.ConnectionAborted)) recipients.Add(u.ToString());
            var name = await db.Users.Where(x => x.Id == User).Select(x => x.DisplayName).SingleAsync(Context.ConnectionAborted);
            await Clients.Users(recipients).SendAsync("typing", new { conversationId, userId = User, name }, Context.ConnectionAborted);
        }
        catch (ApiException) { throw new HubException("Conversation unavailable."); }
        finally { gate.Semaphore.Release(); }
    }
    private async Task Presence()
    {
        var user = User;
        var servers = db.Memberships.Where(x => x.UserId == user).Select(x => x.ServerId);
        var peers = await db.Memberships.Where(x => servers.Contains(x.ServerId)).Select(x => x.UserId).ToListAsync(Context.ConnectionAborted);
        var friends = await db.Friendships.Where(x => x.State == RelationshipState.Accepted && (x.UserA == user || x.UserB == user)).ToListAsync(Context.ConnectionAborted);
        peers.AddRange(friends.Select(x => x.UserA == user ? x.UserB : x.UserA));
        foreach (var peer in peers.Distinct()) if (!await access.Blocked(user, peer, Context.ConnectionAborted)) await Clients.User(peer.ToString()).SendAsync("presence", new { userId = user }, Context.ConnectionAborted);
    }
}
