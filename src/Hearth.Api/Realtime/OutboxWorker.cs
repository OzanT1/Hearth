using System.Diagnostics.Metrics;
using Hearth.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Api.Realtime;

public sealed class OutboxWorker(IServiceScopeFactory scopes, IHubContext<ChatHub> hub, MutationGate gate, ILogger<OutboxWorker> logger) : BackgroundService
{
    public static readonly Meter Meter = new("Hearth.Messaging");
    private static readonly Counter<long> Delivered = Meter.CreateCounter<long>("hearth.outbox.delivered");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("hearth.outbox.failures");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await gate.Semaphore.WaitAsync(stoppingToken);
                try { await Dispatch(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { Failures.Add(1); logger.LogError(exception, "Outbox dispatch failed; events remain pending"); }
                finally { gate.Semaphore.Release(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    private async Task Dispatch(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var access = scope.ServiceProvider.GetRequiredService<Access>();
        var events = await db.Outbox.OrderBy(x => x.Id).Take(50).ToListAsync(ct);
        foreach (var e in events)
        {
            var recipients = new List<Guid>();
            if (e.UserId.HasValue) recipients.Add(e.UserId.Value);
            else if (e.ConversationId.HasValue)
            {
                var c = await db.Conversations.FindAsync([e.ConversationId.Value], ct);
                if (c is not null) recipients.AddRange(await access.Readers(c, ct));
            }
            else if (e.ServerId.HasValue) recipients.AddRange(await db.Memberships.Where(x => x.ServerId == e.ServerId).Select(x => x.UserId).ToListAsync(ct));
            try
            {
                // Invalidation events contain no chat content. Authorized HTTP reads retrieve current state.
                await hub.Clients.Users(recipients.Distinct().Select(x => x.ToString()).ToArray()).SendAsync("changed", new { eventId = e.Id, conversationId = e.ConversationId, kind = e.Kind }, ct);
                db.Outbox.Remove(e); Delivered.Add(1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                e.Attempts++; Failures.Add(1); logger.LogWarning(ex, "Outbox event {EventId} failed on attempt {Attempt}", e.Id, e.Attempts);
            }
        }
        if (events.Count > 0) await db.SaveChangesAsync(ct);
    }
}

public sealed class AttachmentCleanup(IServiceScopeFactory scopes, MutationGate gate, ILogger<AttachmentCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await gate.Semaphore.WaitAsync(stoppingToken);
                try
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
                    var stale = await db.Attachments.Where(x => x.CreatedAt < cutoff && (x.MessageId == null || db.Messages.Any(m => m.Id == x.MessageId && m.Deleted))).ToListAsync(stoppingToken);
                    db.Attachments.RemoveRange(stale); await db.SaveChangesAsync(stoppingToken);
                    var retained = (await db.Attachments.Select(x => x.StorageKey).ToArrayAsync(stoppingToken)).ToHashSet();
                    foreach (var key in storage.Keys()) if (!retained.Contains(key)) storage.Delete(key);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Attachment cleanup failed"); }
                finally { gate.Semaphore.Release(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
