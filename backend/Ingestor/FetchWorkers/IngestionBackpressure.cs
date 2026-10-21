using System.Globalization;
using Data.Logging;
using Ingestor.Options;
using Microsoft.Extensions.Options;

namespace Ingestor.FetchWorkers;

/// <summary>
/// Holds the match-fetch workers' claims back while a host resource is below its threshold
/// (#1459), and announces every activation and release as an ops event.
/// </summary>
/// <remarks>
/// <para>
/// The brake sits in front of the claim and nowhere else, which is what makes it degrade
/// throughput and never correctness: a batch already claimed is fetched and written to the
/// end, so no fetched match is dropped and no claim is left behind — the next claim simply
/// waits. It stops claims rather than slowing them to a fraction, because a fraction keeps
/// filling the disk or the memory that tripped it.
/// </para>
/// <para>
/// One instance is shared by every worker, so a transition is announced once however many
/// workers observe it. A resource releases only once it is <c>ReleaseMarginPercent</c> above
/// its threshold, so a reading hovering at the line does not flap the workers.
/// </para>
/// </remarks>
public sealed class IngestionBackpressure(
    IHostResourceProbe probe,
    IOptions<IngestionBackpressureOptions> options,
    TimeProvider timeProvider,
    ILogger<IngestionBackpressure> logger)
{
    private readonly Lock _gate = new();
    private DateTimeOffset? _activeSince;
    private DateTimeOffset? _firstEvaluation;
    private bool _reportedUnreadable;

    /// <summary>Whether the workers are being held back right now.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _activeSince is not null;
            }
        }
    }

    /// <summary>
    /// Returns once the workers may claim: at once when every resource is above its
    /// threshold, otherwise after reading them again every <c>ProbeIntervalSeconds</c> until
    /// they recover.
    /// </summary>
    public async Task WaitUntilClearAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        while (Evaluate(settings))
        {
            await Task.Delay(TimeSpan.FromSeconds(settings.ProbeIntervalSeconds), timeProvider, ct);
        }
    }

    /// <summary>Reads the resources, records a transition, and says whether claims must wait.</summary>
    private bool Evaluate(IngestionBackpressureOptions settings)
    {
        var reading = probe.Read(settings.DiskPath);

        lock (_gate)
        {
            ReportUnreadable(reading);

            // Hysteresis: the line to cross back over is higher than the line that tripped it.
            var margin = _activeSince is null ? 0 : settings.ReleaseMarginPercent;
            var pressured = new List<string>(2);
            if (reading.AvailableMemoryPercent is { } memory && memory < settings.MinAvailableMemoryPercent + margin)
            {
                pressured.Add(Describe("memory available", memory, settings.MinAvailableMemoryPercent));
            }

            if (reading.FreeDiskPercent is { } disk && disk < settings.MinFreeDiskPercent + margin)
            {
                pressured.Add(Describe("disk free", disk, settings.MinFreeDiskPercent));
            }

            var now = timeProvider.GetUtcNow();
            _firstEvaluation ??= now;
            if (settings.DrillMinutes > 0 && now - _firstEvaluation.Value < TimeSpan.FromMinutes(settings.DrillMinutes))
            {
                pressured.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"drill ({settings.DrillMinutes} min from the first claim, IngestionBackpressure:DrillMinutes)"));
            }

            if (pressured.Count > 0 && _activeSince is null)
            {
                _activeSince = now;
                logger.LogWarning(
                    OpsEvents.IngestionBackpressureActivated,
                    "Ingestion backpressure activated: {Resources}. Match-fetch workers stop claiming until it recovers.",
                    string.Join("; ", pressured));
            }
            else if (pressured.Count == 0 && _activeSince is { } since)
            {
                _activeSince = null;
                logger.LogInformation(
                    OpsEvents.IngestionBackpressureReleased,
                    "Ingestion backpressure released after {DurationSeconds} s (memory available {Memory}, disk free {Disk}). Match-fetch workers claim again.",
                    (long)(now - since).TotalSeconds,
                    Format(reading.AvailableMemoryPercent),
                    Format(reading.FreeDiskPercent));
            }

            return _activeSince is not null;
        }
    }

    private void ReportUnreadable(HostResourceReading reading)
    {
        if (_reportedUnreadable || (reading.AvailableMemoryPercent is not null && reading.FreeDiskPercent is not null))
        {
            return;
        }

        // Once per process: a host that cannot report a resource applies no brake on it, and
        // that is worth one line, not one per claim.
        _reportedUnreadable = true;
        var unreadable = (reading.AvailableMemoryPercent, reading.FreeDiskPercent) switch
        {
            (null, null) => "memory or disk",
            (null, _) => "memory",
            _ => "disk"
        };
        logger.LogWarning(
            "Ingestion backpressure cannot read {Resources} on this host; no backpressure applies to it.",
            unreadable);
    }

    private static string Describe(string resource, double value, double threshold)
        => string.Create(CultureInfo.InvariantCulture, $"{resource} {value:0.#}% (threshold {threshold:0.#}%)");

    private static string Format(double? value)
        => value is { } percent ? string.Create(CultureInfo.InvariantCulture, $"{percent:0.#}%") : "unknown";
}
