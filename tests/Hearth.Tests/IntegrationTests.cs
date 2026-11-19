using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.WebUtilities;
using Hearth.Api;
using Xunit;
using static Hearth.Tests.AppFixture;

namespace Hearth.Tests;

[Trait("Category", "Integration")]
public sealed class IntegrationTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task Csrf_is_required_and_logout_revokes_session()
    {
        var (client, _) = await fixture.Account();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/servers/", new { name = "Blocked" })).StatusCode);
        (await Write(client, "/auth/logout")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Hub_rejects_untrusted_origins_and_requires_authentication()
    {
        var (client, _) = await fixture.Account();
        using var hostile = new HttpRequestMessage(HttpMethod.Post, "/hubs/chat/negotiate?negotiateVersion=1");
        hostile.Headers.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(hostile)).StatusCode);
        using var anonymous = fixture.Factory.CreateClient();
        using var local = new HttpRequestMessage(HttpMethod.Post, "/hubs/chat/negotiate?negotiateVersion=1");
        local.Headers.Add("Sec-Fetch-Site", "same-origin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(local)).StatusCode);
    }

    [Fact]
    public async Task Repeated_failed_logins_lock_out_the_account()
    {
        var (client, id) = await fixture.Account();
        using var scope = fixture.Factory.Services.CreateScope();
        var email = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(id))!.Email!;
        (await Write(client, "/auth/logout")).EnsureSuccessStatusCode();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Write(client, "/auth/login", new { email, password = "incorrect password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Write(client, "/auth/login", new { email, password = "a secure test passphrase" })).StatusCode);
    }
    [Fact]
    public async Task Private_channels_require_allowlists_and_revocation_is_immediate()
    {
        var (owner, _) = await fixture.Account(); var (member, memberId) = await fixture.Account();
        var server = await Json(await Write(owner, "/servers/", new { name = "Privacy test" })); var id = server.GetProperty("id").GetGuid();
        var invite = await Json(await Write(owner, $"/servers/{id}/invites/", new { expiresInHours = 1, maxUses = 2 }));
        (await Write(member, "/invites/redeem", new { token = invite.GetProperty("token").GetString() })).EnsureSuccessStatusCode();
        var channel = await Json(await Write(owner, $"/servers/{id}/channels", new { name = "secret", position = 1, isPrivate = true, roleIds = Array.Empty<Guid>(), userIds = Array.Empty<Guid>() })); var channelId = channel.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/conversations/{channelId}/messages")).StatusCode);
        (await Write(owner, $"/servers/{id}/channels/{channelId}", new { name = "secret", position = 1, isPrivate = true, roleIds = Array.Empty<Guid>(), userIds = new[] { memberId } }, "PUT")).EnsureSuccessStatusCode();
        (await member.GetAsync($"/api/v1/conversations/{channelId}/messages")).EnsureSuccessStatusCode();
        (await Write(owner, $"/servers/{id}/members/{memberId}/kick")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/conversations/{channelId}/messages")).StatusCode);
    }
    [Fact]
    public async Task Messages_are_idempotent_and_deleted_content_is_removed()
    {
        var (client, _) = await fixture.Account();
        var server = await Json(await Write(client, "/servers/", new { name = "Message test" })); var id = server.GetProperty("id").GetGuid();
        var channels = await client.GetFromJsonAsync<JsonElement>($"/api/v1/servers/{id}/channels"); var c = channels[0].GetProperty("id").GetGuid();
        var request = new { clientId = Guid.NewGuid(), content = "hello", attachmentIds = Array.Empty<Guid>() };
        var first = await Json(await Write(client, $"/conversations/{c}/messages", request));
        var second = await Json(await Write(client, $"/conversations/{c}/messages", request));
        Assert.Equal(first.GetProperty("id").GetInt64(), second.GetProperty("id").GetInt64());
        var message = first.GetProperty("id").GetInt64();
        (await Write(client, $"/conversations/{c}/messages/{message}", method: "DELETE")).EnsureSuccessStatusCode();
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{c}/messages");
        Assert.Single(history.GetProperty("items").EnumerateArray()); Assert.Equal("", history.GetProperty("items")[0].GetProperty("content").GetString());
    }
    [Fact]
    public async Task Concurrent_redemption_cannot_exceed_single_use()
    {
        var (owner, _) = await fixture.Account(); var (one, _) = await fixture.Account(); var (two, _) = await fixture.Account();
        var server = await Json(await Write(owner, "/servers/", new { name = "Invite race" })); var id = server.GetProperty("id").GetGuid();
        var invite = await Json(await Write(owner, $"/servers/{id}/invites/", new { expiresInHours = 1, maxUses = 1 }));
        var results = await Task.WhenAll(Write(one, "/invites/redeem", new { token = invite.GetProperty("token").GetString() }), Write(two, "/invites/redeem", new { token = invite.GetProperty("token").GetString() }));
        Assert.Single(results, r => r.IsSuccessStatusCode);
    }
    [Fact]
    public async Task Blocking_stops_contact_but_preserves_history()
    {
        var (one, _) = await fixture.Account(); var (two, target) = await fixture.Account();
        (await Write(one, "/relationships/friends", new { userId = target })).EnsureSuccessStatusCode();
        var friends = await two.GetFromJsonAsync<JsonElement>("/api/v1/relationships/");
        (await Write(two, $"/relationships/friends/{friends[0].GetProperty("id").GetGuid()}/accept")).EnsureSuccessStatusCode();
        var dm = await Json(await Write(one, "/dms", new { userId = target })); var id = dm.GetProperty("id").GetGuid();
        (await Write(one, $"/conversations/{id}/messages", new { clientId = Guid.NewGuid(), content = "hello", attachmentIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        (await Write(one, $"/relationships/blocks/{target}", method: "PUT")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(two, $"/conversations/{id}/messages", new { clientId = Guid.NewGuid(), content = "blocked", attachmentIds = Array.Empty<Guid>() })).StatusCode);
        (await two.GetAsync($"/api/v1/conversations/{id}/messages")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Attachments_enforce_conversation_access_limits_and_deleted_message_tombstones()
    {
        var (owner, _) = await fixture.Account(); var (member, memberId) = await fixture.Account();
        var server = await Community(owner, member);
        var channel = await Json(await Write(owner, $"/servers/{server}/channels", new { name = "files", position = 1, isPrivate = true, roleIds = Array.Empty<Guid>(), userIds = new[] { memberId } }));
        var id = channel.GetProperty("id").GetGuid();
        var uploaded = await Json(await Upload(owner, id, "<script>alert(1)</script>"u8.ToArray(), "notes.html"));
        var file = uploaded.GetProperty("id").GetGuid();
        Assert.Equal("text/plain", uploaded.GetProperty("contentType").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/attachments/{file}")).StatusCode);
        var sent = await Json(await Write(owner, $"/conversations/{id}/messages", new { clientId = Guid.NewGuid(), content = "A file", attachmentIds = new[] { file } }));
        using var download = await member.GetAsync($"/api/v1/attachments/{file}");
        download.EnsureSuccessStatusCode();
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, id, [0, 1, 2, 3], "fake.png")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(owner, id, new byte[10 * 1024 * 1024 + 1], "large.txt")).StatusCode);
        (await Write(owner, $"/servers/{server}/channels/{id}", new { name = "files", position = 1, isPrivate = true, roleIds = Array.Empty<Guid>(), userIds = Array.Empty<Guid>() }, "PUT")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/attachments/{file}")).StatusCode);
        (await Write(owner, $"/conversations/{id}/messages/{sent.GetProperty("id").GetInt64()}", method: "DELETE")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/attachments/{file}")).StatusCode);
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages");
        Assert.Empty(history.GetProperty("items")[0].GetProperty("attachments").EnumerateArray());
    }

    [Fact]
    public async Task Role_management_moderation_and_bans_respect_owner_boundaries()
    {
        var (owner, ownerId) = await fixture.Account(); var (moderator, moderatorId) = await fixture.Account(); var (member, memberId) = await fixture.Account();
        var server = await Community(owner, moderator, member);
        var role = await Json(await Write(owner, $"/servers/{server}/roles", new { name = "Moderator", permissions = 9 }));
        var roleId = role.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(member, $"/servers/{server}/roles", new { name = "Admin", permissions = 15 })).StatusCode);
        (await Write(owner, $"/servers/{server}/roles/{roleId}/members/{moderatorId}", method: "PUT")).EnsureSuccessStatusCode();
        var privateChannel = await Json(await Write(owner, $"/servers/{server}/channels", new { name = "owner-only", position = 1, isPrivate = true, roleIds = Array.Empty<Guid>(), userIds = Array.Empty<Guid>() }));
        var privateId = privateChannel.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.GetAsync($"/api/v1/conversations/{privateId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(moderator, $"/servers/{server}/members/{ownerId}/kick")).StatusCode);
        (await Write(owner, $"/servers/{server}/roles/{roleId}/members/{memberId}", method: "PUT")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(moderator, $"/servers/{server}/members/{memberId}/ban")).StatusCode);
        (await Write(owner, $"/servers/{server}/roles/{roleId}/members/{memberId}", method: "DELETE")).EnsureSuccessStatusCode();
        (await Write(moderator, $"/servers/{server}/members/{memberId}/ban")).EnsureSuccessStatusCode();
        var invite = await Json(await Write(owner, $"/servers/{server}/invites/", new { expiresInHours = 1 }));
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(member, "/invites/redeem", new { token = invite.GetProperty("token").GetString() })).StatusCode);
        (await Write(owner, $"/servers/{server}/bans/{memberId}", method: "DELETE")).EnsureSuccessStatusCode();
        (await Write(member, "/invites/redeem", new { token = invite.GetProperty("token").GetString() })).EnsureSuccessStatusCode();
        var members = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/servers/{server}/members");
        Assert.Empty(members.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == memberId).GetProperty("roleIds").EnumerateArray());
        (await Write(owner, $"/servers/{server}/transfer", new { userId = memberId })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(owner, $"/servers/{server}", method: "DELETE")).StatusCode);
        (await Write(owner, $"/servers/{server}/leave")).EnsureSuccessStatusCode();
        (await Write(member, $"/servers/{server}", method: "DELETE")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.GetAsync($"/api/v1/servers/{server}/channels")).StatusCode);
    }

    [Fact]
    public async Task Privacy_is_rechecked_on_each_send_and_friend_requests_cannot_be_self_accepted()
    {
        var (one, oneId) = await fixture.Account(); var (two, twoId) = await fixture.Account();
        await Community(one, two);
        var dm = await Json(await Write(one, "/dms", new { userId = twoId })); var id = dm.GetProperty("id").GetGuid();
        (await Write(two, "/auth/me", new { displayName = "Friends only", allowSharedServerDms = false }, "PATCH")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(one, $"/conversations/{id}/messages", new { clientId = Guid.NewGuid(), content = "hello", attachmentIds = Array.Empty<Guid>() })).StatusCode);
        (await Write(one, "/relationships/friends", new { userId = twoId })).EnsureSuccessStatusCode();
        var friends = await one.GetFromJsonAsync<JsonElement>("/api/v1/relationships/"); var request = friends[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(one, $"/relationships/friends/{request}/accept")).StatusCode);
        (await Write(two, $"/relationships/friends/{request}/accept")).EnsureSuccessStatusCode();
        (await Write(one, $"/conversations/{id}/messages", new { clientId = Guid.NewGuid(), content = "Allowed friend", attachmentIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        (await Write(two, $"/relationships/blocks/{oneId}", method: "PUT")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(one, "/relationships/friends", new { userId = twoId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(one, $"/conversations/{id}/messages", new { clientId = Guid.NewGuid(), content = "blocked", attachmentIds = Array.Empty<Guid>() })).StatusCode);
        (await one.GetAsync($"/api/v1/conversations/{id}/messages")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cursor_pagination_and_read_positions_are_monotonic()
    {
        var (owner, ownerId) = await fixture.Account(); var (member, _) = await fixture.Account();
        var server = await Community(owner, member);
        var channels = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/servers/{server}/channels"); var id = channels[0].GetProperty("id").GetGuid();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Messages.AddRange(Enumerable.Range(0, 55).Select(i => new Message { ConversationId = id, AuthorId = ownerId, ClientId = Guid.NewGuid(), Content = $"message {i}" }));
            await db.SaveChangesAsync();
        }
        var first = await member.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages");
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetInt64();
        var second = await member.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages?before={cursor}");
        Assert.Equal(5, second.GetProperty("items").GetArrayLength()); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.True(second.GetProperty("items")[4].GetProperty("id").GetInt64() < cursor);
        (await Write(member, $"/conversations/{id}/read", new { messageId = first.GetProperty("items")[49].GetProperty("id").GetInt64() }, "PUT")).EnsureSuccessStatusCode();
        (await Write(member, $"/conversations/{id}/read", new { messageId = second.GetProperty("items")[0].GetProperty("id").GetInt64() }, "PUT")).EnsureSuccessStatusCode();
        var after = await member.GetFromJsonAsync<JsonElement>($"/api/v1/servers/{server}/channels"); Assert.Equal(0, after[0].GetProperty("unreadCount").GetInt64());
    }

    [Fact]
    public async Task Password_recovery_revokes_sessions_and_tokens_cannot_be_reused()
    {
        var (client, id) = await fixture.Account();
        using var scope = fixture.Factory.Services.CreateScope();
        var email = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(id))!.Email!;
        (await Write(client, "/auth/forgot", new { email })).EnsureSuccessStatusCode();
        var parameters = QueryHelpers.ParseQuery(new Uri(fixture.Mail.Links[email]).Query);
        var reset = new { userId = id, token = parameters["token"].ToString(), password = "a new secure passphrase" };
        (await Write(client, "/auth/reset", reset)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, "/auth/reset", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Write(client, "/auth/login", new { email, password = "a secure test passphrase" })).StatusCode);
        (await Write(client, "/auth/login", new { email, password = "a new secure passphrase" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await Write(client, "/auth/forgot", new { email = "absent@example.test" })).StatusCode);
    }

    [Fact]
    public async Task Authenticator_setup_enable_recovery_and_disable_work_without_bypasses()
    {
        var (client, id) = await fixture.Account();
        using var scope = fixture.Factory.Services.CreateScope();
        var email = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(id))!.Email!;
        var setup = await Json(await Write(client, "/auth/2fa/setup", new { password = "a secure test passphrase" }));
        var secret = setup.GetProperty("key").GetString()!;
        (await client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();
        var enabled = await Json(await Write(client, "/auth/2fa/enable", new { code = Totp(secret) }));
        var recovery = enabled.GetProperty("recoveryCodes")[0].GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        var login = await Json(await Write(client, "/auth/login", new { email, password = "a secure test passphrase" }));
        Assert.True(login.GetProperty("requiresTwoFactor").GetBoolean());
        (await Write(client, "/auth/login", new { email, password = "a secure test passphrase", recoveryCode = recovery })).EnsureSuccessStatusCode();
        (await Write(client, "/auth/2fa/disable", new { code = Totp(secret) })).EnsureSuccessStatusCode();
        login = await Json(await Write(client, "/auth/login", new { email, password = "a secure test passphrase" }));
        Assert.False(login.GetProperty("requiresTwoFactor").GetBoolean());
    }

    [Fact]
    public async Task Registration_enforces_approved_emails_and_account_cap_with_generic_responses()
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..15];
        var allowed = name + "@example.test"; var second = name + "2@example.test"; var blocked = name + "x@example.test";
        int count;
        using (var scope = fixture.Factory.Services.CreateScope())
            count = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync();
        await using var limited = fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.PostConfigure<AppOptions>(o =>
        {
            o.MaxRegisteredUsers = count + 1; o.RegistrationAllowedEmails = [allowed, second];
        })));
        using var client = limited.CreateClient();
        var denied = await Json(await Write(client, "/auth/register", new { email = blocked, userName = name + "x", password = "a secure test passphrase" }));
        var accepted = await Json(await Write(client, "/auth/register", new { email = allowed, userName = name, password = "a secure test passphrase" }));
        var full = await Json(await Write(client, "/auth/register", new { email = second, userName = name + "2", password = "a secure test passphrase" }));
        Assert.Equal(denied.GetRawText(), accepted.GetRawText()); Assert.Equal(full.GetRawText(), accepted.GetRawText());
        Assert.True(fixture.Mail.Links.ContainsKey(allowed)); Assert.False(fixture.Mail.Links.ContainsKey(blocked)); Assert.False(fixture.Mail.Links.ContainsKey(second));
        using var check = fixture.Factory.Services.CreateScope();
        Assert.Equal(count + 1, await check.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Repeated_recovery_requests_do_not_flood_the_mailbox()
    {
        var (client, id) = await fixture.Account();
        using var scope = fixture.Factory.Services.CreateScope();
        var email = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(id))!.Email!;
        var before = fixture.Mail.Sends[email];
        (await Write(client, "/auth/forgot", new { email })).EnsureSuccessStatusCode();
        (await Write(client, "/auth/forgot", new { email })).EnsureSuccessStatusCode();
        Assert.Equal(before + 1, fixture.Mail.Sends[email]);
    }

    [Fact]
    public async Task Invalid_inputs_and_missing_csrf_are_rejected_before_waiting_for_authorization_gate()
    {
        var (client, _) = await fixture.Account();
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, "/auth/login", new { email = "test@example.test", password = new string('a', 129) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, "/conversations/" + Guid.NewGuid() + "/messages", new { clientId = Guid.NewGuid(), content = "", attachmentIds = new Guid[11] })).StatusCode);
        var gate = fixture.Factory.Services.GetRequiredService<MutationGate>();
        await gate.Semaphore.WaitAsync();
        try
        {
            using var rejected = await client.PostAsJsonAsync("/api/v1/servers/", new { name = "Missing CSRF" }).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var busy = await client.GetAsync("/api/v1/auth/me").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Equal("2", busy.Headers.GetValues("Retry-After").Single());
        }
        finally { gate.Semaphore.Release(); }
        (await client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Attachment_quotas_reject_further_writes_and_preserve_existing_files()
    {
        var (owner, _) = await fixture.Account(); var server = await Community(owner);
        var channels = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/servers/{server}/channels"); var id = channels[0].GetProperty("id").GetGuid();
        var options = fixture.Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppOptions>>().Value;
        var previous = options.MaxUserStorageMb; options.MaxUserStorageMb = 10;
        try
        {
            var bytes = Enumerable.Repeat((byte)'a', 6 * 1024 * 1024).ToArray();
            var uploaded = await Json(await Upload(owner, id, bytes, "first.txt"));
            var denied = await Upload(owner, id, bytes, "second.txt");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, denied.StatusCode);
            using var download = await owner.GetAsync($"/api/v1/attachments/{uploaded.GetProperty("id").GetGuid()}");
            download.EnsureSuccessStatusCode(); Assert.Equal(bytes.Length, (await download.Content.ReadAsByteArrayAsync()).Length);
        }
        finally { options.MaxUserStorageMb = previous; }
    }

    private static string Totp(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(secret.Select(c => Convert.ToString(alphabet.IndexOf(char.ToUpperInvariant(c)), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30); if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter); var offset = hash[^1] & 15;
        var value = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1000000).ToString("D6");
    }

    [Fact]
    public async Task Ten_users_can_send_concurrently_without_losing_messages()
    {
        var accounts = new List<HttpClient>();
        for (var i = 0; i < 10; i++) accounts.Add((await fixture.Account()).Client);
        var server = await Community(accounts[0], accounts.Skip(1).ToArray());
        var channels = await accounts[0].GetFromJsonAsync<JsonElement>($"/api/v1/servers/{server}/channels");
        var id = channels[0].GetProperty("id").GetGuid();
        var responses = await Task.WhenAll(accounts.Select((client, i) => Write(client, $"/conversations/{id}/messages",
            new { clientId = Guid.NewGuid(), content = $"Concurrent user {i}", attachmentIds = Array.Empty<Guid>() })));
        foreach (var response in responses) response.EnsureSuccessStatusCode();
        var history = await accounts[9].GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages");
        var messages = history.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(10, messages.Length);
        Assert.Equal(10, messages.Select(x => x.GetProperty("authorId").GetGuid()).Distinct().Count());
    }

    private static async Task<Guid> Community(HttpClient owner, params HttpClient[] members)
    {
        var server = await Json(await Write(owner, "/servers/", new { name = "Integration community" })); var id = server.GetProperty("id").GetGuid();
        var invite = await Json(await Write(owner, $"/servers/{id}/invites/", new { expiresInHours = 1 }));
        foreach (var member in members) (await Write(member, "/invites/redeem", new { token = invite.GetProperty("token").GetString() })).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, Guid conversation, byte[] bytes, string name)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var content = new MultipartFormDataContent(); var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); content.Add(file, "file", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/conversations/{conversation}/attachments") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
}
