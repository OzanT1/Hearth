using System.Security.Claims;
using System.Text;
using Hearth.Api.Contracts;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hearth.Api.Features;

public static class Attachments
{
    public static void MapAttachments(this RouteGroupBuilder api)
    {
        api.MapPost("/conversations/{id:guid}/attachments", async (Guid id, HttpRequest request, ClaimsPrincipal p, Access a, AppDbContext db, IFileStorage storage, IOptions<AppOptions> options, CancellationToken ct) =>
        {
            await a.Send(id, p.UserId(), ct);
            Guard.Against(!request.HasFormContentType, "A file is required.");
            var form = await request.ReadFormAsync(ct); Guard.Against(form.Files.Count != 1, "Upload one file at a time.");
            var file = form.Files[0]; Guard.Against(file.Length == 0 || file.Length > options.Value.MaxUploadMb * 1024L * 1024L, "File exceeds the upload size limit.", 413);
            var userBytes = await db.Attachments.Where(x => x.UploaderId == p.UserId()).SumAsync(x => (long?)x.Size, ct) ?? 0;
            Guard.Against(userBytes + file.Length > options.Value.MaxUserStorageMb * 1024L * 1024L, "Your attachment storage quota is full.", 413);
            Guard.Against(storage.UsedBytes + file.Length > options.Value.MaxStorageMb * 1024L * 1024L, "Attachment storage is full. Contact the administrator.", 413);
            var pending = await db.Attachments.CountAsync(x => x.UploaderId == p.UserId() && x.MessageId == null, ct);
            Guard.Against(pending >= 30, "Too many pending attachments. Send or remove existing uploads.", 429);
            await using var input = file.OpenReadStream(); using var buffer = new MemoryStream(); await input.CopyToAsync(buffer, ct);
            var type = Detect(buffer.ToArray()); Guard.Against(type is null, "Supported files: PNG, JPEG, WebP, PDF, and UTF-8 text.");
            var attachment = new Attachment { ConversationId = id, UploaderId = p.UserId(), FileName = Path.GetFileName(file.FileName).Replace('\r', '_').Replace('\n', '_'), ContentType = type!, StorageKey = Guid.NewGuid().ToString("N"), Size = file.Length };
            Guard.Against(attachment.FileName.Length is 0 or > 200, "Invalid filename.");
            buffer.Position = 0; await storage.Write(attachment.StorageKey, buffer, ct);
            try { db.Attachments.Add(attachment); await db.SaveChangesAsync(ct); } catch { storage.Delete(attachment.StorageKey); throw; }
            return TypedResults.Ok(new AttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size));
        }).RequireAuthorization().RequireRateLimiting("uploads").WithMetadata(new UploadEndpoint()).WithTags("Attachments");
        api.MapGet("/attachments/{id:guid}", async (Guid id, ClaimsPrincipal p, Access a, AppDbContext db, IFileStorage storage, HttpResponse response, CancellationToken ct) =>
        {
            var file = Guard.Found(await db.Attachments.FindAsync([id], ct)); await a.Read(file.ConversationId, p.UserId(), ct);
            if (file.MessageId is null) Guard.Against(file.UploaderId != p.UserId(), "Resource not found.", 404);
            else Guard.Against(!await db.Messages.AnyAsync(x => x.Id == file.MessageId && !x.Deleted, ct), "Resource not found.", 404);
            response.Headers.CacheControl = "no-store"; response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Results.File(storage.Open(file.StorageKey), file.ContentType, fileDownloadName: file.ContentType.StartsWith("image/", StringComparison.Ordinal) ? null : file.FileName, enableRangeProcessing: false);
        }).RequireAuthorization().WithTags("Attachments");
        api.MapDelete("/attachments/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var file = Guard.Found(await db.Attachments.SingleOrDefaultAsync(x => x.Id == id && x.UploaderId == p.UserId() && x.MessageId == null, ct));
            db.Attachments.Remove(file); await db.SaveChangesAsync(ct); storage.Delete(file.StorageKey); return TypedResults.NoContent();
        }).RequireAuthorization().WithTags("Attachments");
    }
    public static string? Detect(byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (data.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (data.StartsWith("%PDF-"u8)) return "application/pdf";
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) return null;
            // Text is always downloaded with nosniff, never interpreted as HTML/SVG.
            return "text/plain";
        }
        catch (DecoderFallbackException) { return null; }
    }
}
