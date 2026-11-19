using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Hearth.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Hearth.Tests;

public sealed class TestMail : IEmailService
{
    public ConcurrentDictionary<string, string> Links { get; } = new();
    public ConcurrentDictionary<string, int> Sends { get; } = new();
    public Task SendLink(string to, string subject, string link, CancellationToken ct)
    { Links[to] = link; Sends.AddOrUpdate(to, 1, (_, count) => count + 1); return Task.CompletedTask; }
}
public sealed class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer redis = new RedisBuilder("redis:7.4-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public TestMail Mail { get; } = new();
    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = postgres.GetConnectionString(), ["ConnectionStrings:Redis"] = redis.GetConnectionString(),
                ["App:DataPath"] = Path.Combine(Path.GetTempPath(), "hearth-tests", Guid.NewGuid().ToString("N")), ["App:PublicUrl"] = "http://localhost",
                ["App:MaxRegisteredUsers"] = "1000"
            }));
            builder.ConfigureServices(services => { services.RemoveAll<IEmailService>(); services.AddSingleton<IEmailService>(Mail); });
        });
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync() { await Factory.DisposeAsync(); await postgres.DisposeAsync(); await redis.DisposeAsync(); }
    public async Task<(HttpClient Client, Guid Id)> Account()
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..15]; var email = name + "@example.test";
        var client = Factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        (await Write(client, "/auth/register", new { email, userName = name, password = "a secure test passphrase" })).EnsureSuccessStatusCode();
        var parameters = QueryHelpers.ParseQuery(new Uri(Mail.Links[email]).Query);
        (await Write(client, "/auth/verify", new { userId = parameters["userId"].ToString(), token = parameters["token"].ToString() })).EnsureSuccessStatusCode();
        (await Write(client, "/auth/login", new { email, password = "a secure test passphrase" })).EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        return (client, me.GetProperty("id").GetGuid());
    }
    public static async Task<HttpResponseMessage> Write(HttpClient client, string path, object? body = null, string method = "POST")
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1" + path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    public static async Task<JsonElement> Json(HttpResponseMessage response) { response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
}
