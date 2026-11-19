using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hearth.Api;
using Hearth.Api.Features;
using Hearth.Api.Realtime;
using Hearth.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args.Where(a => a != "--migrate").ToArray());
builder.Logging.AddJsonConsole();
builder.Services.AddOptions<AppOptions>().BindConfiguration("App").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<MailOptions>().BindConfiguration("Mail").ValidateDataAnnotations().ValidateOnStart();
var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ?? new();
if (builder.Environment.IsProduction())
{
    ProductionConfiguration.Validate(builder.Configuration, appOptions);
}
builder.Services.AddDbContext<AppDbContext>((services, o) => o.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.")));
builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
    o.Password.RequiredLength = 12; o.Password.RequireDigit = false; o.Password.RequireNonAlphanumeric = false; o.Password.RequireUppercase = false; o.Password.RequireLowercase = false;
    o.SignIn.RequireConfirmedEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = builder.Environment.IsProduction() ? "__Host-hearth.session" : "hearth.session";
    o.Cookie.Path = "/"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
{
    o.Cookie.Name = builder.Environment.IsProduction() ? "__Host-hearth.2fa" : "hearth.2fa";
    o.Cookie.Path = "/"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = builder.Environment.IsProduction() ? "__Host-hearth.csrf" : "hearth.csrf"; o.Cookie.Path = "/"; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddDataProtection().SetApplicationName("Hearth").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appOptions.DataPath, "keys")));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = appOptions.MaxUploadMb * 1024L * 1024L + 65536);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = appOptions.MaxUploadMb * 1024L * 1024L + 65536);
builder.WebHost.ConfigureKestrel(o => { o.AddServerHeader = false; o.Limits.MaxConcurrentConnections = 100; o.Limits.MaxConcurrentUpgradedConnections = 50; });
builder.Services.AddRequestTimeouts(o => o.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(30), TimeoutStatusCode = 504 });
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false"));
builder.Services.AddSingleton<EphemeralState>(); builder.Services.AddSingleton<IFileStorage, LocalFileStorage>(); builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<Access>(); builder.Services.AddSingleton<MutationGate>(); builder.Services.AddSingleton<Connections>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EmailThrottle>();
builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 4096; o.MaximumParallelInvocationsPerClient = 1; o.EnableDetailedErrors = false; });
builder.Services.AddHostedService<OutboxWorker>(); builder.Services.AddHostedService<AttachmentCleanup>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>(); builder.Services.AddProblemDetails(); builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = (context, _) => { context.HttpContext.Response.Headers.RetryAfter = "60"; return ValueTask.CompletedTask; };
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = builder.Environment.IsEnvironment("Testing") ? 10000 : 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("messages", context => RateLimitPartition.GetTokenBucketLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new() { TokenLimit = 15, TokensPerPeriod = 5, ReplenishmentPeriod = TimeSpan.FromSeconds(5), AutoReplenishment = true, QueueLimit = 0 }));
    o.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new() { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("email", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = builder.Environment.IsProduction() ? 5 : 10000, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })),
        PartitionedRateLimiter.Create<HttpContext, string>(c => c.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetConcurrencyLimiter("api", _ => new() { PermitLimit = 20, QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other")));
});
builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService("hearth-api"))
    .WithTracing(t => { t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation(); if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not null) t.AddOtlpExporter(); })
    .WithMetrics(m => { m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter("Hearth.Messaging"); if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not null) m.AddOtlpExporter(); });
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
    var proxy = builder.Configuration["App:TrustedProxy"];
    if (!string.IsNullOrEmpty(proxy)) o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    else o.ForwardedHeaders = ForwardedHeaders.None;
});
var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(); return;
}
app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs") ||
        context.Request.Path.StartsWithSegments("/verify") || context.Request.Path.StartsWithSegments("/reset"))
        context.Response.Headers.CacheControl = "no-cache, no-store";
    if (app.Environment.IsProduction() && !context.Request.IsHttps && !context.Request.Path.StartsWithSegments("/health"))
    { context.Response.StatusCode = 400; return; }
    if (context.Request.Path.StartsWithSegments("/hubs"))
    {
        var origin = context.Request.Headers.Origin.ToString();
        var sameOriginFetch = string.IsNullOrEmpty(origin) && !context.WebSockets.IsWebSocketRequest && context.Request.Headers["Sec-Fetch-Site"] == "same-origin";
        if (origin != appOptions.PublicUrl.TrimEnd('/') && !sameOriginFetch) { context.Response.StatusCode = 403; return; }
    }
    await next(context);
});
app.UseRouting();
app.UseRequestTimeouts();
app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
    // Only the upload endpoint needs a multipart-sized body. Bound JSON before binding.
    if (context.GetEndpoint()?.Metadata.GetMetadata<UploadEndpoint>() is null &&
        context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        size.MaxRequestBodySize = 65536;
    if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
    {
        if (string.IsNullOrEmpty(context.Request.Headers["X-CSRF-TOKEN"]))
        { await Results.Problem(statusCode: 400, title: "Invalid CSRF token. Refresh and retry.").ExecuteAsync(context); return; }
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "Invalid CSRF token. Refresh and retry.").ExecuteAsync(context); return; }
    }
    if (context.GetEndpoint()?.Metadata.GetMetadata<UploadEndpoint>() is not null && context.Request.HasFormContentType)
        await context.Request.ReadFormAsync(context.RequestAborted);
    await next(context);
});
app.UseDefaultFiles(); app.UseStaticFiles();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>().AddEndpointFilter<SerializedApiFilter>();
api.MapAccounts(); api.MapServers(); api.MapChannels(); api.MapInvitations(); api.MapRelationships(); api.MapMessaging(); api.MapAttachments();
app.MapHub<ChatHub>("/hubs/chat", o => o.CloseOnAuthenticationExpiration = true);
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (AppDbContext db, IConnectionMultiplexer redis, CancellationToken ct) =>
{
    if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503);
    if (await db.Database.GetPendingMigrationsAsync(ct) is var pending && pending.Any()) return Results.StatusCode(503);
    return Results.Ok(new { status = redis.IsConnected ? "healthy" : "degraded", redis = redis.IsConnected });
});
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
await app.RunAsync();
public partial class Program { }
