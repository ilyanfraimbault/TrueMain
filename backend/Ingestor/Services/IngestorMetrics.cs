using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Ingestor.Options;

namespace Ingestor.Services;

/// <summary>
/// Owns the ingestor's <see cref="Meter"/> and the instruments published from it (#260).
/// </summary>
/// <remarks>
/// The meter is built from the DI <see cref="IMeterFactory"/> rather than a static
/// <c>new Meter(...)</c>: the factory scopes the meter to the container, disposes it with
/// the host, and is what the metrics pipeline (and the in-memory test collectors) key on.
/// A statically cached meter would outlive the host and be invisible to both.
/// </remarks>
public sealed class IngestorMetrics
{
    /// <summary>Meter name to enable when scraping the ingestor (OTLP, Prometheus, dotnet-counters).</summary>
    public const string MeterName = "TrueMain.Ingestor";

    /// <summary>Name of the failure counter emitted on every swallowed ingestion failure.</summary>
    public const string RunFailuresCounterName = "ingestor.run.failures";

    /// <summary>Name of the histogram recording how long a Riot call waited for a rate-limit permit (#1359).</summary>
    public const string RiotRateLimitWaitHistogramName = "ingestor.riot.ratelimit.wait";

    /// <summary>Name of the counter incremented on every Riot 429 (#1359).</summary>
    public const string RiotRateLimitRejectionsCounterName = "ingestor.riot.ratelimit.rejections";

    /// <summary>
    /// Value of the <c>process</c> tag when a failure cannot be attributed to a single
    /// named process — the run itself broke (mode resolution, process index build, ...).
    /// The parentheses keep it from ever colliding with a real <c>IIngestorProcess.Name</c>,
    /// and a constant sentinel keeps the tag set uniform, which Prometheus-style backends
    /// need to treat every increment as the same series.
    /// </summary>
    public const string WholeRunProcess = "(run)";

    private readonly Counter<long> _runFailures;
    private readonly Histogram<double> _riotRateLimitWait;
    private readonly Counter<long> _riotRateLimitRejections;

    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "IMeterFactory owns the meters it creates and disposes them with the container; "
            + "disposing here would tear down the meter while the host is still running.")]
    public IngestorMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _runFailures = meter.CreateCounter<long>(
            RunFailuresCounterName,
            unit: "{failure}",
            description: "Ingestion failures swallowed by the worker loop, tagged by failing process and job mode.");

        _riotRateLimitWait = meter.CreateHistogram<double>(
            RiotRateLimitWaitHistogramName,
            unit: "ms",
            description: "Time a Riot API call spent waiting for a rate-limit permit, tagged by routing value and endpoint.");

        _riotRateLimitRejections = meter.CreateCounter<long>(
            RiotRateLimitRejectionsCounterName,
            unit: "{rejection}",
            description: "Riot API 429 responses, tagged by routing value, endpoint and the limit type Riot attributed them to.");
    }

    /// <summary>
    /// Records one swallowed ingestion failure.
    /// </summary>
    /// <param name="process">
    /// The failing <c>IIngestorProcess.Name</c>, or <see cref="WholeRunProcess"/> when the
    /// whole run failed outside any individual process.
    /// </param>
    /// <param name="mode">The job mode the worker was running.</param>
    public void RecordRunFailure(string process, JobMode mode)
    {
        _runFailures.Add(
            1,
            new KeyValuePair<string, object?>("process", process),
            // The enum is stringified rather than boxed: exporters render tag values as
            // strings anyway, and a boxed enum surfaces as its numeric value in some of them.
            new KeyValuePair<string, object?>("mode", mode.ToString()));
    }

    /// <summary>
    /// Records how long a Riot call was held back by the rate limiter before it was sent.
    /// Only called when the wait was non-zero, so the histogram describes throttled calls
    /// rather than being flooded with zeros by the calls that sailed through.
    /// </summary>
    /// <param name="routingValue">Riot routing value the budget belongs to, e.g. <c>europe</c>.</param>
    /// <param name="endpoint">Riot method key, e.g. <c>match-v5.timeline</c>.</param>
    /// <param name="wait">How long the call waited for its permit.</param>
    public void RecordRiotRateLimitWait(string routingValue, string endpoint, TimeSpan wait)
    {
        _riotRateLimitWait.Record(
            wait.TotalMilliseconds,
            new KeyValuePair<string, object?>("routing_value", routingValue),
            new KeyValuePair<string, object?>("endpoint", endpoint));
    }

    /// <summary>
    /// Records one Riot 429. With the limiter in place this should be close to zero; a
    /// rising count means our model of the budget and Riot's have drifted apart.
    /// </summary>
    /// <param name="routingValue">Riot routing value the call was made on.</param>
    /// <param name="endpoint">Riot method key the call targeted.</param>
    /// <param name="limitType">The <c>X-Rate-Limit-Type</c> Riot attributed it to, or <c>unknown</c>.</param>
    public void RecordRiotRateLimitRejection(string routingValue, string endpoint, string limitType)
    {
        _riotRateLimitRejections.Add(
            1,
            new KeyValuePair<string, object?>("routing_value", routingValue),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("limit_type", limitType));
    }
}
