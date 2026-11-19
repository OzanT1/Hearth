using System.Security.Claims;
using System.Text;
using Hearth.Api.Contracts;
using Hearth.Api.Realtime;
using Hearth.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Hearth.Api.Features;

public static class Accounts
{
    public static void MapAccounts(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Accounts");
        var security = group.MapGroup("").RequireRateLimiting("auth");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => TypedResults.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        security.MapPost("/register", async (RegisterRequest request, UserManager<AppUser> users, IEmailService email, IOptions<AppOptions> options, EmailThrottle throttle, CancellationToken ct) =>
        {
            var allowed = options.Value.RegistrationAllowedEmails;
            if ((allowed.Length > 0 && !allowed.Contains(request.Email, StringComparer.OrdinalIgnoreCase)) ||
                await users.FindByEmailAsync(request.Email) is not null || await users.Users.CountAsync(ct) >= options.Value.MaxRegisteredUsers)
                return TypedResults.Ok(new { message = "Check your email, or use account recovery if you already registered." });
            var user = new AppUser { Id = Guid.NewGuid(), Email = request.Email, UserName = request.UserName, DisplayName = request.UserName };
            Check(await users.CreateAsync(user, request.Password));
            await Verification(user, users, email, options.Value, throttle, ct);
            return TypedResults.Ok(new { message = "Check your email, or use account recovery if you already registered." });
        }).RequireRateLimiting("email");
        security.MapPost("/verify", async (TokenRequest request, UserManager<AppUser> users) =>
        {
            var user = Guard.Found(await users.FindByIdAsync(request.UserId.ToString()));
            Check(await users.ConfirmEmailAsync(user, Decode(request.Token)));
            return TypedResults.NoContent();
        });
        security.MapPost("/resend", async (EmailRequest request, UserManager<AppUser> users, IEmailService email, IOptions<AppOptions> options, EmailThrottle throttle, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(request.Email);
            if (user is { EmailConfirmed: false }) await Verification(user, users, email, options.Value, throttle, ct);
            return TypedResults.NoContent();
        }).RequireRateLimiting("email");
        security.MapPost("/login", async (LoginRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            var user = await users.FindByEmailAsync(request.Email);
            Guard.Against(user is null, "Invalid credentials or unverified account.", 401);
            var result = await signIn.PasswordSignInAsync(user!, request.Password, false, true);
            if (result.RequiresTwoFactor)
            {
                if (!string.IsNullOrEmpty(request.RecoveryCode)) result = await signIn.TwoFactorRecoveryCodeSignInAsync(request.RecoveryCode);
                else if (!string.IsNullOrEmpty(request.Code)) result = await signIn.TwoFactorAuthenticatorSignInAsync(request.Code.Replace(" ", ""), false, false);
                else return Results.Ok(new { requiresTwoFactor = true });
            }
            Guard.Against(!result.Succeeded, "Invalid credentials or unverified account.", 401);
            return Results.Ok(new { requiresTwoFactor = false });
        });
        security.MapPost("/forgot", async (EmailRequest request, UserManager<AppUser> users, IEmailService email, IOptions<AppOptions> options, EmailThrottle throttle, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(request.Email);
            if (user is { EmailConfirmed: true })
            {
                var token = Encode(await users.GeneratePasswordResetTokenAsync(user));
                await SendLink(user.Id, "reset", user.Email!, "Reset your Hearth password", $"{options.Value.PublicUrl.TrimEnd('/')}/reset?userId={user.Id}&token={token}", email, throttle, ct);
            }
            return TypedResults.NoContent();
        }).RequireRateLimiting("email");
        security.MapPost("/reset", async (ResetRequest request, UserManager<AppUser> users, Connections connections) =>
        {
            var user = Guard.Found(await users.FindByIdAsync(request.UserId.ToString()));
            Check(await users.ResetPasswordAsync(user, Decode(request.Token), request.Password));
            connections.Revoke(user.Id);
            return TypedResults.NoContent();
        });
        group.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) => TypedResults.Ok(ToDto(Guard.Found(await users.GetUserAsync(principal))))).RequireAuthorization();
        group.MapPatch("/me", async (ProfileRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db) =>
        {
            var user = Guard.Found(await users.GetUserAsync(principal));
            user.DisplayName = request.DisplayName.Trim();
            Guard.Against(user.DisplayName.Length == 0, "Display name is required.");
            user.AllowSharedServerDms = request.AllowSharedServerDms;
            Check(await users.UpdateAsync(user));
            db.Publish(userId: user.Id);
            await db.SaveChangesAsync();
            return TypedResults.Ok(ToDto(user));
        }).RequireAuthorization();
        security.MapPost("/logout", async (ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, Connections connections) =>
        {
            var user = Guard.Found(await users.GetUserAsync(principal));
            Check(await users.UpdateSecurityStampAsync(user));
            connections.Revoke(user.Id);
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization();
        security.MapPost("/2fa/setup", async (PasswordRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, Connections connections) =>
        {
            var user = Guard.Found(await users.GetUserAsync(principal));
            Guard.Against(!await users.CheckPasswordAsync(user, request.Password), "Invalid credentials.", 403);
            Guard.Against(user.TwoFactorEnabled, "Two-factor authentication is already enabled.");
            Check(await users.ResetAuthenticatorKeyAsync(user));
            connections.Revoke(user.Id);
            await signIn.RefreshSignInAsync(user);
            var key = await users.GetAuthenticatorKeyAsync(user);
            return TypedResults.Ok(new { key, uri = $"otpauth://totp/Hearth:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer=Hearth&digits=6" });
        }).RequireAuthorization();
        security.MapPost("/2fa/enable", async (TwoFactorRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, Connections connections) =>
        {
            var user = Guard.Found(await users.GetUserAsync(principal));
            Guard.Against(!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code.Replace(" ", "")), "Invalid authenticator code.");
            Check(await users.SetTwoFactorEnabledAsync(user, true));
            var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            Check(await users.UpdateSecurityStampAsync(user));
            connections.Revoke(user.Id);
            await signIn.SignOutAsync();
            return TypedResults.Ok(new { recoveryCodes = codes });
        }).RequireAuthorization();
        security.MapPost("/2fa/disable", async (TwoFactorRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, Connections connections) =>
        {
            var user = Guard.Found(await users.GetUserAsync(principal));
            Guard.Against(!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code.Replace(" ", "")), "Invalid authenticator code.");
            Check(await users.SetTwoFactorEnabledAsync(user, false));
            Check(await users.ResetAuthenticatorKeyAsync(user));
            Check(await users.UpdateSecurityStampAsync(user));
            connections.Revoke(user.Id);
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization();
    }
    private static UserDto ToDto(AppUser user) => new(user.Id, user.UserName!, user.DisplayName, user.AllowSharedServerDms);
    private static void Check(IdentityResult result) => Guard.Against(!result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { throw new ApiException(400, "Invalid or expired token."); }
    }
    private static async Task Verification(AppUser user, UserManager<AppUser> users, IEmailService email, AppOptions options, EmailThrottle throttle, CancellationToken ct)
    {
        var token = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        await SendLink(user.Id, "verify", user.Email!, "Verify your Hearth account", $"{options.PublicUrl.TrimEnd('/')}/verify?userId={user.Id}&token={token}", email, throttle, ct);
    }
    private static async Task SendLink(Guid user, string purpose, string to, string subject, string link, IEmailService email, EmailThrottle throttle, CancellationToken ct)
    {
        if (!throttle.TryAcquire(user, purpose)) return;
        try { await email.SendLink(to, subject, link, ct); }
        catch { throttle.Release(user, purpose); throw; }
    }
}
