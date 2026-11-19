using System.Security.Claims;
using Hearth.Api.Contracts;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hearth.Api.Features;

public static class Messaging
{
    public static void MapMessaging(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/conversations/{id:guid}").RequireAuthorization().WithTags("Messaging");
        g.MapGet("/messages", async (Guid id, long? before, ClaimsPrincipal p, AppDbContext db, Access a, CancellationToken ct) =>
        {
            await a.Read(id, p.UserId(), ct);
            var rows = await db.Messages.AsNoTracking().Where(x => x.ConversationId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(51).ToListAsync(ct);
            var more = rows.Count > 50; rows = rows.Take(50).Reverse().ToList();
            return TypedResults.Ok(new MessagePage(await Dtos(rows, p.UserId(), db, ct), more ? rows[0].Id : null));
        });
        g.MapPost("/messages", async (Guid id, SendMessageRequest r, ClaimsPrincipal p, AppDbContext db, Access a, IOptions<AppOptions> options, CancellationToken ct) =>
        {
            var user = p.UserId(); await a.Send(id, user, ct);
            Guard.Against(r.ClientId == Guid.Empty || r.AttachmentIds is null || r.Content is null, "Invalid message.");
            var existing = await db.Messages.SingleOrDefaultAsync(x => x.AuthorId == user && x.ClientId == r.ClientId, ct);
            if (existing is not null)
            {
                Guard.Against(existing.ConversationId != id, "Message identifier is already in use.", 409);
                return TypedResults.Ok((await Dtos([existing], user, db, ct))[0]);
            }
            var ids = r.AttachmentIds!.Distinct().ToArray(); var content = r.Content!.Trim();
            Guard.Against((content.Length == 0 && ids.Length == 0) || ids.Length > options.Value.MaxAttachments, "Write a message or attach a file within the attachment limit.");
            if (r.ReplyToId.HasValue) Guard.Against(!await db.Messages.AnyAsync(x => x.Id == r.ReplyToId && x.ConversationId == id, ct), "Reply must refer to a message in this conversation.");
            var files = await db.Attachments.Where(x => ids.Contains(x.Id) && x.ConversationId == id && x.UploaderId == user && x.MessageId == null).ToListAsync(ct);
            Guard.Against(files.Count != ids.Length, "Invalid attachments.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var message = new Message { ConversationId = id, AuthorId = user, ClientId = r.ClientId, Content = content, ReplyToId = r.ReplyToId };
            db.Messages.Add(message); await db.SaveChangesAsync(ct);
            foreach (var file in files) file.MessageId = message.Id;
            db.Publish(conversationId: id, kind: "message"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return TypedResults.Ok((await Dtos([message], user, db, ct))[0]);
        }).RequireRateLimiting("messages");
        g.MapPatch("/messages/{messageId:long}", async (Guid id, long messageId, EditMessageRequest r, ClaimsPrincipal p, AppDbContext db, Access a, CancellationToken ct) =>
        {
            await a.Send(id, p.UserId(), ct); var m = Guard.Found(await db.Messages.SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == id, ct));
            Guard.Against(m.AuthorId != p.UserId() || m.Deleted, "You cannot edit this message.", 403);
            m.Content = r.Content.Trim(); Guard.Against(m.Content.Length == 0, "Message cannot be blank."); m.EditedAt = DateTimeOffset.UtcNow;
            db.Publish(conversationId: id, kind: "message"); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapDelete("/messages/{messageId:long}", async (Guid id, long messageId, ClaimsPrincipal p, AppDbContext db, Access a, CancellationToken ct) =>
        {
            var c = await a.Read(id, p.UserId(), ct); var m = Guard.Found(await db.Messages.SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == id, ct));
            if (m.AuthorId != p.UserId()) { Guard.Against(c.ServerId is null, "You cannot delete this message.", 403); await a.Require(c.ServerId!.Value, p.UserId(), Permission.ModerateMessages, ct); }
            m.Content = ""; m.Deleted = true; m.EditedAt = DateTimeOffset.UtcNow;
            db.Reactions.RemoveRange(await db.Reactions.Where(x => x.MessageId == messageId).ToListAsync(ct));
            db.Publish(conversationId: id, kind: "message"); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapPut("/messages/{messageId:long}/reactions", async (Guid id, long messageId, ReactionRequest r, ClaimsPrincipal p, AppDbContext db, Access a, CancellationToken ct) =>
        {
            await a.Send(id, p.UserId(), ct); Guard.Against(!Rules.Emojis.Contains(r.Emoji), "Unsupported reaction.");
            Guard.Found(await db.Messages.SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == id && !x.Deleted, ct));
            var reaction = await db.Reactions.FindAsync([messageId, p.UserId(), r.Emoji], ct);
            if (reaction is null) db.Reactions.Add(new() { MessageId = messageId, UserId = p.UserId(), Emoji = r.Emoji }); else db.Reactions.Remove(reaction);
            db.Publish(conversationId: id, kind: "reaction"); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
        g.MapPut("/read", async (Guid id, ReadRequest r, ClaimsPrincipal p, AppDbContext db, Access a, CancellationToken ct) =>
        {
            await a.Read(id, p.UserId(), ct);
            Guard.Against(!await db.Messages.AnyAsync(x => x.Id == r.MessageId && x.ConversationId == id, ct), "Invalid read position.");
            var position = await db.ReadPositions.FindAsync([id, p.UserId()], ct);
            if (position is null) db.ReadPositions.Add(new() { ConversationId = id, UserId = p.UserId(), MessageId = r.MessageId }); else position.MessageId = Math.Max(position.MessageId, r.MessageId);
            db.Publish(userId: p.UserId(), kind: "read"); await db.SaveChangesAsync(ct); return TypedResults.NoContent();
        });
    }
    private static async Task<MessageDto[]> Dtos(List<Message> messages, Guid user, AppDbContext db, CancellationToken ct)
    {
        var ids = messages.Select(x => x.Id).ToArray(); var authors = messages.Select(x => x.AuthorId).Distinct().ToArray();
        var names = await db.Users.Where(x => authors.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var blocked = await db.Blocks.Where(x => x.UserId == user).Select(x => x.TargetId).ToArrayAsync(ct);
        var files = await db.Attachments.Where(x => x.MessageId != null && ids.Contains(x.MessageId.Value)).ToListAsync(ct);
        var reactions = await db.Reactions.Where(x => ids.Contains(x.MessageId)).ToListAsync(ct);
        return messages.Select(m => new MessageDto(m.Id, m.ConversationId, m.AuthorId, names[m.AuthorId], m.ClientId, m.Deleted ? "" : m.Content, m.ReplyToId, m.CreatedAt, m.EditedAt, m.Deleted, blocked.Contains(m.AuthorId), m.Deleted ? [] : files.Where(x => x.MessageId == m.Id).Select(x => new AttachmentDto(x.Id, x.FileName, x.ContentType, x.Size)).ToArray(), m.Deleted ? [] : reactions.Where(x => x.MessageId == m.Id).GroupBy(x => x.Emoji).Select(g => new ReactionDto(g.Key, g.Count(), g.Any(x => x.UserId == user))).ToArray())).ToArray();
    }
}
