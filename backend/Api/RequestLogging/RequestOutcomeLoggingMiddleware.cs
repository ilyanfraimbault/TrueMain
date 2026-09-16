using System.Diagnostics;
using Data.Logging;

namespace TrueMain.RequestLogging;

/// <summary>
/// Logs the requests that went wrong — a 5xx answer, or a client that gave up
/// before the answer — with the method, path, status, duration and traceId an
/// operator needs to find them (#1555).
/// </summary>
/// <remarks>
/// <para>
/// Registered as the outermost middleware, ahead of the exception handler, so it
/// sees the final status of every request, including the 500 that handler writes
/// for an unhandled exception. The handler's own log line carries the exception;
/// this row carries the request, and the traceId ties the two together.
/// </para>
/// <para>
/// Successful and 4xx requests are not logged: full request logging was left out
/// on purpose (the ops logs are signal-only), and a 429 is reported by the
/// rate-limit recorder, which rolls the flood up per visitor.
/// </para>
/// <para>
/// The traceId is <c>HttpContext.TraceIdentifier</c>, the value ProblemDetails
/// returns to the client, not the W3C activity id.
/// </para>
/// </remarks>
public sealed class RequestOutcomeLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestOutcomeLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var threw = false;
        try
        {
            await next(context);
        }
        catch
        {
            threw = true;
            throw;
        }
        finally
        {
            Report(context, threw, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private void Report(HttpContext context, bool threw, long durationMs)
    {
        var request = context.Request;
        var path = request.Path.Value + request.QueryString.Value;

        if (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(
                OpsEvents.RequestAborted,
                "{RequestMethod} {RequestPath} was abandoned by the client after {DurationMs} ms (trace {TraceId})",
                request.Method,
                path,
                durationMs,
                context.TraceIdentifier);
            return;
        }

        // An exception that escaped every handler still reaches the client as a 500
        // when nothing was written yet, even though the response object says 200.
        var statusCode = threw && !context.Response.HasStarted
            ? StatusCodes.Status500InternalServerError
            : context.Response.StatusCode;
        if (statusCode < StatusCodes.Status500InternalServerError)
        {
            return;
        }

        logger.LogError(
            OpsEvents.RequestFailed,
            "{RequestMethod} {RequestPath} answered {StatusCode} in {DurationMs} ms (trace {TraceId})",
            request.Method,
            path,
            statusCode,
            durationMs,
            context.TraceIdentifier);
    }
}
