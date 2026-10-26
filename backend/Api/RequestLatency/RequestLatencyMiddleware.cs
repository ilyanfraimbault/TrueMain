using System.Diagnostics;

namespace TrueMain.RequestLatency;

/// <summary>
/// Times every request that routes to an ops endpoint and hands it to the
/// <see cref="RequestLatencyAccumulator"/> (#2307), keyed by the route template so
/// <c>/ops/accounts/{puuid}</c> is one row, not one per account.
/// </summary>
/// <remarks>
/// Registered ahead of the exception handler, like the request-outcome logging, so the 500
/// that handler writes is the status it sees. The endpoint is read before the rest of the
/// pipeline runs: the exception handler clears it when it re-executes.
/// </remarks>
public sealed class RequestLatencyMiddleware(
    RequestDelegate next,
    RequestLatencyAccumulator accumulator,
    TimeProvider timeProvider)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var route = OpsRouteOf(context.GetEndpoint());
        if (route is null)
        {
            await next(context);
            return;
        }

        var startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var startedAt = Stopwatch.GetTimestamp();
        var threw = false;
        var cancelled = false;
        try
        {
            await next(context);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch
        {
            threw = true;
            throw;
        }
        finally
        {
            var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            accumulator.Record(Classify(context, route, startedAtUtc, durationMs, threw, cancelled));
        }
    }

    /// <summary>
    /// The template of an ops endpoint with a leading slash, or null for any other request —
    /// the public routes, the health probes, a path that matched nothing.
    /// </summary>
    internal static string? OpsRouteOf(Endpoint? endpoint)
    {
        if (endpoint is not RouteEndpoint { RoutePattern.RawText: { } template })
        {
            return null;
        }

        var normalized = "/" + template.TrimStart('/');
        return normalized.StartsWith("/ops/", StringComparison.OrdinalIgnoreCase) ? normalized : null;
    }

    internal static RequestLatencySample Classify(
        HttpContext context,
        string route,
        DateTime startedAtUtc,
        double durationMs,
        bool threw,
        bool cancelled)
    {
        // An exception that escaped every handler still reaches the client as a 500 when
        // nothing was written yet, even though the response object says 200.
        var statusCode = (threw || cancelled) && !context.Response.HasStarted
            ? StatusCodes.Status500InternalServerError
            : context.Response.StatusCode;
        var timedOut = cancelled
            || context.RequestAborted.IsCancellationRequested
            || statusCode is StatusCodes.Status504GatewayTimeout or StatusCodes.Status408RequestTimeout;

        return new RequestLatencySample(
            route,
            context.Request.Method,
            startedAtUtc,
            durationMs,
            timedOut,
            ServerError: !timedOut && statusCode >= StatusCodes.Status500InternalServerError);
    }
}
