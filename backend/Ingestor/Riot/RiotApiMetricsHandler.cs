using System.Diagnostics;
using System.Globalization;
using Data.Metrics.Mongo;

namespace Ingestor.Riot;

/// <summary>
/// <see cref="DelegatingHandler"/> that records one Riot API usage metric per HTTP
/// attempt (#93): endpoint key, status code, latency and rate-limit response
/// headers. Registered on each typed Riot client <em>inside</em> the resilience
/// handler, so it sees every physical attempt — including the retried 429s the
/// resilience pipeline backs off on — which is exactly what the rate-limit and
/// status-code views need.
/// </summary>
/// <remarks>
/// Recording is fire-and-forget and fully defensive: it only enqueues onto a
/// bounded in-memory channel and every failure is swallowed, so instrumentation
/// can never slow down, fault or change the outcome of a Riot call.
/// </remarks>
internal sealed class RiotApiMetricsHandler(IRiotApiCallRecorder recorder) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var start = Stopwatch.GetTimestamp();
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            return response;
        }
        finally
        {
            // Reaching this handler means a physical send was attempted (the circuit
            // breaker / rate limiter sit further out), so even a faulted attempt is a
            // real call worth recording — with status 0 to mark "no response".
            Record(request, response, Stopwatch.GetElapsedTime(start), startedAtUtc);
        }
    }

    private void Record(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        TimeSpan elapsed,
        DateTime startedAtUtc)
    {
        try
        {
            var (endpoint, route) = RiotEndpointClassifier.Classify(request);
            var statusCode = response is null ? 0 : (int)response.StatusCode;

            recorder.Record(new RiotApiCallRecord(
                startedAtUtc,
                endpoint,
                request.Method.Method,
                statusCode,
                (long)elapsed.TotalMilliseconds,
                route,
                Header(response, "X-App-Rate-Limit"),
                Header(response, "X-App-Rate-Limit-Count"),
                Header(response, "X-Method-Rate-Limit"),
                Header(response, "X-Method-Rate-Limit-Count"),
                RetryAfterSeconds(response),
                Header(response, "X-Rate-Limit-Type")));
        }
        catch
        {
            // Telemetry must never surface as a failure on the Riot call path.
        }
    }

    private static string? Header(HttpResponseMessage? response, string name)
        => response is not null && response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private static int? RetryAfterSeconds(HttpResponseMessage? response)
    {
        // Riot returns Retry-After as an integer number of seconds on 429/503.
        // Prefer the parsed delta; fall back to the raw header for robustness.
        var delta = response?.Headers.RetryAfter?.Delta;
        if (delta is not null)
        {
            // Clamp before the cast: a multi-week Delta would otherwise overflow
            // int (Riot only ever sends seconds/minutes, but stay defensive).
            return (int)Math.Min(delta.Value.TotalSeconds, int.MaxValue);
        }

        var raw = Header(response, "Retry-After");
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : null;
    }
}
