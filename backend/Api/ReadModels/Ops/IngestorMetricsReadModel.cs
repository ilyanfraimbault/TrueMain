namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The Ingestor's own meter over a window, for the admin Riot API tab (#1636): every
/// instrument it recorded, each split by tag set. Summed from the per-minute rollups its
/// meter exporter writes; an instrument that recorded nothing in the window is absent.
/// </summary>
public sealed record IngestorMetricsReadModel
{
    /// <summary>The resolved window key: <c>1h</c> / <c>24h</c> / <c>7d</c> / <c>30d</c>.</summary>
    public string Window { get; init; } = string.Empty;

    public DateTime SinceUtc { get; init; }

    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>The rollup retention the API is configured with, in days; null when the TTL is disabled.</summary>
    public double? RetentionDays { get; init; }

    /// <summary>
    /// The oldest rollup still stored; null when none is, meaning nothing was ever recorded —
    /// not a quiet window. Measuring started with #1636, so a window reaching further back
    /// than this was only partly measured.
    /// </summary>
    public DateTime? OldestRetainedUtc { get; init; }

    /// <summary>Instruments by name.</summary>
    public IReadOnlyList<IngestorInstrumentReadModel> Instruments { get; init; } = [];
}

/// <summary>One instrument over the window, its series by total descending.</summary>
public sealed record IngestorInstrumentReadModel
{
    /// <summary>The instrument name, e.g. <c>ingestor.riot.ratelimit.wait</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary><c>counter</c> or <c>histogram</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    public string? Unit { get; init; }

    public string? Description { get; init; }

    /// <summary>Measurements recorded across every series.</summary>
    public long Count { get; init; }

    /// <summary>Their sum: a counter's total, a histogram's summed value.</summary>
    public double Sum { get; init; }

    /// <summary>The largest single measurement.</summary>
    public double Max { get; init; }

    public IReadOnlyList<IngestorMeterSeriesReadModel> Series { get; init; } = [];
}

/// <summary>One tag set of an instrument over the window.</summary>
public sealed record IngestorMeterSeriesReadModel
{
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

    public long Count { get; init; }

    public double Sum { get; init; }

    /// <summary><see cref="Sum"/> / <see cref="Count"/>: a histogram's mean measurement.</summary>
    public double Mean { get; init; }

    public double Max { get; init; }

    public DateTime LastRecordedAtUtc { get; init; }
}
