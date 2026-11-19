using System.ComponentModel.DataAnnotations;
using System.Net;
using Hearth.Infrastructure;
using Npgsql;

namespace Hearth.Api;

public static class ProductionConfiguration
{
    public static void Validate(IConfiguration configuration, AppOptions options)
    {
        if (!Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.IsLoopback || origin.HostNameType != UriHostNameType.Dns || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("Production App:PublicUrl must be a public HTTPS origin without a path, query, or credentials.");

        var hosts = (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(h => h.Contains('*')) || !hosts.Contains(origin.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production AllowedHosts must explicitly include the public hostname without wildcards.");
        if (!IPAddress.TryParse(configuration["App:TrustedProxy"], out _))
            throw new InvalidOperationException("Production App:TrustedProxy must identify the HTTPS reverse proxy.");
        if (!configuration.GetValue<bool>("Mail:UseTls"))
            throw new InvalidOperationException("Production SMTP must use TLS.");
        var mail = configuration.GetSection("Mail").Get<MailOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(mail.Host) || mail.Host is "localhost" or "mailpit" ||
            string.IsNullOrWhiteSpace(mail.User) || string.IsNullOrWhiteSpace(mail.Password) || mail.Password == "replace-me" ||
            !MimeKit.MailboxAddress.TryParse(mail.From, out var sender) || sender.Address.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configure a production SMTP host, sender, and credentials.");

        var postgres = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Postgres"));
        if (string.IsNullOrWhiteSpace(postgres.Host) || string.IsNullOrWhiteSpace(postgres.Database) ||
            string.IsNullOrWhiteSpace(postgres.Username) || postgres.Password is null || postgres.Password.Length < 24 ||
            postgres.Password.Contains("replace-", StringComparison.OrdinalIgnoreCase) || postgres.Password == "local-development-only")
            throw new InvalidOperationException("Configure production database credentials with a random password of at least 24 characters.");

        var redis = StackExchange.Redis.ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "");
        if (string.IsNullOrWhiteSpace(redis.Password) || redis.Password.Length < 24 ||
            redis.Password.Contains("replace-", StringComparison.OrdinalIgnoreCase) || redis.Password == postgres.Password)
            throw new InvalidOperationException("Configure a distinct random Redis password of at least 24 characters.");

        var allowed = options.RegistrationAllowedEmails.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
        if (allowed.Length == 0 || allowed.Length > options.MaxRegisteredUsers ||
            allowed.Any(e => !new EmailAddressAttribute().IsValid(e) || e != e.Trim()))
            throw new InvalidOperationException("Production App:RegistrationAllowedEmails must list the approved users (up to App:MaxRegisteredUsers).");
    }
}
