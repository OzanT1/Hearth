using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using StackExchange.Redis;

namespace Hearth.Infrastructure;

public sealed class AppOptions
{
    [Required, Url] public string PublicUrl { get; set; } = "http://localhost:5173";
    [Required] public string DataPath { get; set; } = "data";
    [Range(1, 50)] public int MaxUploadMb { get; set; } = 10;
    [Range(1, 10)] public int MaxAttachments { get; set; } = 5;
    [Range(1, 1000)] public int MaxRegisteredUsers { get; set; } = 10;
    public string[] RegistrationAllowedEmails { get; set; } = [];
    [Range(10, 100000)] public int MaxStorageMb { get; set; } = 2048;
    [Range(10, 100000)] public int MaxUserStorageMb { get; set; } = 256;
}
public sealed class MailOptions
{
    [Required] public string Host { get; set; } = "localhost";
    [Range(1, 65535)] public int Port { get; set; } = 1025;
    [Required] public string From { get; set; } = "hello@hearth.local";
    public bool UseTls { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
}
public interface IEmailService
{
    Task SendLink(string to, string subject, string link, CancellationToken ct);
}
public sealed class SmtpEmailService(IOptions<MailOptions> options) : IEmailService
{
    public async Task SendLink(string to, string subject, string link, CancellationToken ct)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(o.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = $"{subject}\n\n{link}\n\nIf you didn't request this, ignore this email.", HtmlBody = $"<p>{HtmlEncoder.Default.Encode(subject)}</p><p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Continue to Hearth</a></p><p>If you didn't request this, ignore this email.</p>" }.ToMessageBody();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new SmtpClient { Timeout = 10000 };
        await client.ConnectAsync(o.Host, o.Port, o.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, timeout.Token);
        if (!string.IsNullOrEmpty(o.User)) await client.AuthenticateAsync(o.User, o.Password ?? "", timeout.Token);
        await client.SendAsync(message, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}
public interface IFileStorage
{
    Task Write(string key, Stream stream, CancellationToken ct);
    Stream Open(string key);
    void Delete(string key);
    IEnumerable<string> Keys();
    long UsedBytes { get; }
}
public sealed class LocalFileStorage(IOptions<AppOptions> options) : IFileStorage
{
    private string Root => Path.GetFullPath(Path.Combine(options.Value.DataPath, "uploads"));
    private string FilePath(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _)) throw new ArgumentException("Invalid storage key.");
        return Path.Combine(Root, key);
    }
    public async Task Write(string key, Stream stream, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var path = FilePath(key); var created = false;
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            created = true;
            await stream.CopyToAsync(file, ct);
        }
        catch { if (created) File.Delete(path); throw; }
    }
    public Stream Open(string key) => new FileStream(FilePath(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
    public void Delete(string key) => File.Delete(FilePath(key));
    public IEnumerable<string> Keys() => Directory.Exists(Root) ? Directory.EnumerateFiles(Root).Select(Path.GetFileName).OfType<string>() : [];
    public long UsedBytes => Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles().Sum(f => f.Length) : 0;
}
public sealed class EphemeralState(IConnectionMultiplexer redis)
{
    public async Task Heartbeat(Guid user, string connection)
    {
        if (!redis.IsConnected) return;
        var db = redis.GetDatabase();
        await db.SortedSetAddAsync($"presence:{user}", connection, DateTimeOffset.UtcNow.AddSeconds(70).ToUnixTimeSeconds());
        await db.KeyExpireAsync($"presence:{user}", TimeSpan.FromMinutes(2));
    }
    public Task Disconnect(Guid user, string connection) => redis.IsConnected ? redis.GetDatabase().SortedSetRemoveAsync($"presence:{user}", connection) : Task.CompletedTask;
    public async Task<bool> Online(Guid user) => redis.IsConnected && await redis.GetDatabase().SortedSetLengthAsync($"presence:{user}", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), double.PositiveInfinity) > 0;
    public Task<bool> Typing(Guid conversation, Guid user) => redis.IsConnected ? redis.GetDatabase().StringSetAsync($"typing:{conversation}:{user}", "1", TimeSpan.FromSeconds(3), When.NotExists) : Task.FromResult(false);
}
