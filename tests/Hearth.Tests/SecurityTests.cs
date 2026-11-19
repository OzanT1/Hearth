using Hearth.Api;
using Hearth.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Hearth.Tests;

public sealed class SecurityTests
{
    private static (IConfigurationRoot Config, AppOptions Options) Production(string? key = null, string? value = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "chat.example.com", ["App:TrustedProxy"] = "172.30.0.2",
            ["Mail:UseTls"] = "true", ["Mail:Host"] = "smtp.example.com", ["Mail:From"] = "Hearth <hello@example.com>",
            ["Mail:User"] = "smtp-user", ["Mail:Password"] = "test-credential-never-used",
            ["ConnectionStrings:Postgres"] = "Host=postgres;Database=hearth;Username=hearth;Password=abcdefghijklmnopqrstuvwxyz123456",
            ["ConnectionStrings:Redis"] = "redis:6379,password=abcdefghijklmnopqrstuvwxyz654321"
        };
        if (key is not null) settings[key] = value;
        return (new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new()
        {
            PublicUrl = "https://chat.example.com", RegistrationAllowedEmails = ["friend@example.com", ""]
        });
    }

    [Fact]
    public void Production_accepts_explicit_origin_proxy_mail_and_approved_users()
    {
        var (config, options) = Production();
        ProductionConfiguration.Validate(config, options);
    }

    [Theory]
    [InlineData("http://chat.example.com")]
    [InlineData("https://localhost")]
    [InlineData("https://user:password@chat.example.com")]
    [InlineData("https://chat.example.com/path")]
    [InlineData("https://chat.example.com/?token=value")]
    [InlineData("https://chat.example.com/#fragment")]
    public void Production_rejects_unsafe_public_origins(string origin)
    {
        var (config, options) = Production(); options.PublicUrl = origin;
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(config, options));
    }

    [Theory]
    [InlineData("AllowedHosts", "*")]
    [InlineData("AllowedHosts", "different.example.com")]
    [InlineData("App:TrustedProxy", "")]
    [InlineData("Mail:UseTls", "false")]
    [InlineData("Mail:Password", "replace-me")]
    [InlineData("ConnectionStrings:Redis", "redis:6379")]
    [InlineData("ConnectionStrings:Redis", "redis:6379,password=abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("ConnectionStrings:Postgres", "Host=postgres;Database=hearth;Username=hearth;Password=local-development-only")]
    public void Production_rejects_insecure_configuration(string key, string value)
    {
        var (config, options) = Production(key, value);
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(config, options));
    }

    [Fact]
    public void Production_requires_an_email_allowlist_within_the_account_limit()
    {
        var (config, options) = Production(); options.RegistrationAllowedEmails = [];
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(config, options));
        options.RegistrationAllowedEmails = ["one@example.com", "two@example.com"]; options.MaxRegisteredUsers = 1;
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(config, options));
    }

    [Fact]
    public void Email_cooldown_is_per_account_and_purpose_and_expires()
    {
        var time = new TestTime(); var throttle = new EmailThrottle(time); var user = Guid.NewGuid();
        Assert.True(throttle.TryAcquire(user, "verify"));
        Assert.False(throttle.TryAcquire(user, "verify"));
        Assert.True(throttle.TryAcquire(user, "reset"));
        throttle.Release(user, "reset"); Assert.True(throttle.TryAcquire(user, "reset"));
        time.Now = time.Now.AddMinutes(5); Assert.True(throttle.TryAcquire(user, "verify"));
    }

    [Fact]
    public async Task Authorization_gate_is_released_before_result_streaming()
    {
        var gate = new MutationGate(); var filter = new SerializedApiFilter(gate);
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext());
        await filter.InvokeAsync(context, _ =>
        {
            Assert.Equal(0, gate.Semaphore.CurrentCount);
            return ValueTask.FromResult<object?>(Results.File(new MemoryStream([1, 2, 3])));
        });
        Assert.Equal(1, gate.Semaphore.CurrentCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await filter.InvokeAsync(context, _ => throw new InvalidOperationException("failed handler")));
        Assert.Equal(1, gate.Semaphore.CurrentCount);
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
