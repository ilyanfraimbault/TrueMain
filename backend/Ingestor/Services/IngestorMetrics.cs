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

    /// <summary>
    /// Value of the <c>process</c> tag when a failure cannot be attributed to a single
    /// named process — the run itself broke (mode resolution, process index build, ...).
    /// The parentheses keep it from ever colliding with a real <c>IIngestorProcess.Name</c>,
    /// and a constant sentinel keeps the tag set uniform, which Prometheus-style backends
    /// need to treat every increment as the same series.
    /// </summary>
    public const string WholeRunProcess = "(run)";

    private readonly Counter<long> _runFailures;

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
}
