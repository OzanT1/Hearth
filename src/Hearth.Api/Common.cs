using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Hearth.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearth.Api;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public static class Guard
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void Against(bool condition, string message, int status = 400) { if (condition) throw new ApiException(status, message); }
    public static T Found<T>(T? value) where T : class => value ?? throw new ApiException(404, "Resource not found.");
}
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            ApiException e => e.Status,
            BadHttpRequestException e => e.StatusCode,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => 409,
            PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } => 409,
            _ => 500
        };
        if (status == 500) logger.LogError(exception, "Request failed: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new() { Status = status, Title = status == 500 ? "An unexpected error occurred." : exception is ApiException ? exception.Message : status == 409 ? "A concurrent change occurred. Refresh and retry." : "Invalid request.", Extensions = { ["traceId"] = context.TraceIdentifier } }
        });
    }
}
public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments.Where(a => a?.GetType().Namespace == "Hearth.Api.Contracts"))
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(argument!, new ValidationContext(argument!), results, true))
                return Results.ValidationProblem(results.SelectMany(r => r.MemberNames.DefaultIfEmpty("request").Select(n => (n, r.ErrorMessage!))).GroupBy(r => r.n).ToDictionary(g => g.Key, g => g.Select(r => r.Item2).ToArray()));
        }
        return await next(context);
    }
}
// A single-replica gate makes authorization changes atomic with live recipient checks.
// PostgreSQL constraints/transactions additionally protect durable invariants.
public sealed class MutationGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }

public sealed class UploadEndpoint { }

// Model binding and response streaming must never hold the authorization gate.
public sealed class SerializedApiFilter(MutationGate gate) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!await gate.Semaphore.WaitAsync(TimeSpan.FromSeconds(2), context.HttpContext.RequestAborted))
        {
            context.HttpContext.Response.Headers.RetryAfter = "2";
            return Results.Problem(statusCode: 503, title: "The server is busy. Please retry shortly.");
        }
        try { return await next(context); }
        finally { gate.Semaphore.Release(); }
    }
}

// At most the configured accounts can receive mail. Entries expire instead of growing indefinitely.
public sealed class EmailThrottle(TimeProvider time)
{
    private readonly Dictionary<(Guid User, string Purpose), DateTimeOffset> sent = new();
    public bool TryAcquire(Guid user, string purpose)
    {
        var now = time.GetUtcNow();
        lock (sent)
        {
            foreach (var expired in sent.Where(x => x.Value <= now).Select(x => x.Key).ToArray()) sent.Remove(expired);
            if (sent.ContainsKey((user, purpose))) return false;
            sent[(user, purpose)] = now.AddMinutes(5);
            return true;
        }
    }
    public void Release(Guid user, string purpose) { lock (sent) sent.Remove((user, purpose)); }
}
