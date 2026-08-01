namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The automated anomaly detectors behind the admin data-quality panel (#924): one card
/// per detector, each carrying its own verdict, headline number and drill-down rows.
///
/// <para>
/// The shape is deliberately uniform — a detector is a status, a number and a list of
/// rows — so the panel renders any number of detectors with one component, and adding
/// the next detector is a backend-only change. The prose lives here rather than in the
/// front end because a threshold and the sentence explaining it have to move together;
/// this is an operator-only, English-only surface (no i18n), so there is nothing to be
/// gained by splitting them across two repositories' worth of files.
/// </para>
/// </summary>
public sealed record DataQualityDetectorsReadModel
{
    /// <summary>One entry per detector, in a stable presentation order.</summary>
    public IReadOnlyList<DataQualityDetectorReadModel> Detectors { get; init; } = [];

    /// <summary>
    /// When the detectors ran. Every age on the panel is relative to this, so a stale
    /// browser tab cannot silently age its own numbers.
    /// </summary>
    public DateTime EvaluatedAtUtc { get; init; }
}

/// <summary>One detector's card.</summary>
public sealed record DataQualityDetectorReadModel
{
    /// <summary>Stable camelCase key (e.g. <c>duplicateDimensionRows</c>).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Short display title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// <c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c>. Unknown means the
    /// detector could not compute its answer — it is never used for "measured and fine".
    /// </summary>
    public string Status { get; init; } = "unknown";

    /// <summary>
    /// The card's headline number, or null when unknown. What it counts is stated by
    /// <see cref="CountLabel"/> and differs per detector (duplicate groups, stale
    /// aggregations, orphan rows in the sample…).
    /// </summary>
    public long? Count { get; init; }

    /// <summary>What <see cref="Count"/> counts, as a noun phrase.</summary>
    public string CountLabel { get; init; } = string.Empty;

    /// <summary>One sentence answering "so what": the verdict in words, with the numbers in it.</summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>
    /// Why the answer is unknown. Set if and only if <see cref="Status"/> is
    /// <c>unknown</c>, so the panel can always explain the state instead of showing a
    /// blank card.
    /// </summary>
    public string? UnknownReason { get; init; }

    /// <summary>
    /// The detector's data source and cost, for the operator reading the panel: which
    /// tables it looks at and why that is affordable on a page view.
    /// </summary>
    public string SourceNote { get; init; } = string.Empty;

    /// <summary>The drill-down: one row per audited table / platform / family / patch.</summary>
    public IReadOnlyList<DataQualityDetectorRowReadModel> Rows { get; init; } = [];

    /// <summary>
    /// The configured levels this detector judged against, echoed so the panel states the
    /// line it drew rather than leaving the operator to guess it from a colour.
    /// </summary>
    public IReadOnlyList<DataQualityThresholdReadModel> Thresholds { get; init; } = [];

    /// <summary>
    /// True when a heavier, on-demand endpoint can expand this detector (currently only
    /// the per-champion freshness breakdown), so the panel knows whether to offer it.
    /// </summary>
    public bool HasDrillDownEndpoint { get; init; }
}

/// <summary>
/// One drill-down row. Deliberately generic: a label, its own verdict, a number and a
/// pre-formatted qualifier, which covers "a dimension table with N duplicate groups",
/// "a platform whose newest match is 3 h old" and "a patch with 412 matches" without a
/// per-detector payload shape.
/// </summary>
public sealed record DataQualityDetectorRowReadModel
{
    /// <summary>Table, platform, process, patch or check name.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary><c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c> for this row alone.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>The row's primary number, or null when it could not be measured.</summary>
    public double? Value { get; init; }

    /// <summary>
    /// The row's number as the panel should print it (with its unit), or null when
    /// unmeasured. Formatted here so a percentage, a count and an age can share one row
    /// type without the front end branching on the detector.
    /// </summary>
    public string? ValueLabel { get; init; }

    /// <summary>Why this row reads the way it does, or what it is measuring.</summary>
    public string? Note { get; init; }
}

/// <summary>One configured green/amber/red boundary, echoed for display.</summary>
public sealed record DataQualityThresholdReadModel
{
    /// <summary>What the levels apply to (e.g. "duplicate groups").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Amber level, or null when disabled.</summary>
    public double? Amber { get; init; }

    /// <summary>Red level, or null when disabled.</summary>
    public double? Red { get; init; }

    /// <summary>Unit of the two levels: <c>count</c>, <c>percent</c>, <c>hours</c> or <c>ratio</c>.</summary>
    public string Unit { get; init; } = "count";
}

/// <summary>
/// The on-demand per-champion aggregate-freshness breakdown
/// (<c>GET /ops/data-quality/aggregate-freshness</c>). Split off the detector payload
/// because it is the one measurement that needs a grouped scan of
/// <c>champion_aggregate_scopes</c> — affordable on an explicit click, not on every page
/// view.
/// </summary>
public sealed record AggregateFreshnessReadModel
{
    /// <summary>Patches covered, newest first.</summary>
    public IReadOnlyList<string> Patches { get; init; } = [];

    /// <summary>
    /// Champions whose newest aggregate row is oldest first, capped by
    /// <c>DataQualityDetectors:FreshnessChampionLimit</c>.
    /// </summary>
    public IReadOnlyList<ChampionFreshnessRowReadModel> Champions { get; init; } = [];

    /// <summary>Distinct champions with at least one aggregate row in the covered patches.</summary>
    public int ChampionCount { get; init; }

    /// <summary>Champions whose newest row is older than the amber cadence.</summary>
    public int StaleChampionCount { get; init; }

    /// <summary>The amber cadence, in hours, this breakdown judged against.</summary>
    public double StaleAfterHours { get; init; }

    /// <summary>When the breakdown was computed.</summary>
    public DateTime EvaluatedAtUtc { get; init; }
}

/// <summary>One champion's aggregate freshness on a patch.</summary>
public sealed record ChampionFreshnessRowReadModel
{
    /// <summary>Riot champion id (the admin resolves names from static data).</summary>
    public int ChampionId { get; init; }

    /// <summary>Normalised "MAJOR.MINOR" patch.</summary>
    public string Patch { get; init; } = string.Empty;

    /// <summary>Newest <c>AggregatedAtUtc</c> across the champion's scope rows on that patch.</summary>
    public DateTime LastAggregatedAtUtc { get; init; }

    /// <summary>Hours since that refresh.</summary>
    public double AgeHours { get; init; }

    /// <summary>Scope rows behind the reading, so a one-account champion reads as thin rather than broken.</summary>
    public long ScopeRows { get; init; }

    /// <summary><c>green</c> | <c>amber</c> | <c>red</c> for this champion's age.</summary>
    public string Status { get; init; } = "unknown";
}
