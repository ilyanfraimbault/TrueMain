namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The operator cockpit payload (#1031): one rolled-up verdict, one line per signal,
/// and the raw measurements behind them.
///
/// <para>
/// This is the "is the pipeline healthy right now?" answer in a single response. It
/// composes the signals that already have their own admin panels — the data-quality
/// detectors, the per-process run rollup, the disk forecast — rather than re-measuring
/// them, so a tile can never disagree with the page it links to. The verdict lives here
/// and not in the frontend because thresholds are a domain decision, and because a
/// second consumer (alerting) must be able to ask the same question and get the same
/// answer.
/// </para>
/// </summary>
public sealed record PipelineHealthReadModel
{
    /// <summary>
    /// The single verdict: <c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c>. The
    /// worst of <see cref="Signals"/> with precedence <c>red &gt; amber &gt; unknown &gt;
    /// green</c> — the same vocabulary and the same precedence as the data-quality
    /// detectors, so one dot means one thing across the whole portal.
    /// </summary>
    public string Status { get; init; } = "unknown";

    /// <summary>
    /// The verdict as one sentence an operator can act on ("All 5 signals pass", "2
    /// signals are failing"). Never blank.
    /// </summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>
    /// When these signals were measured. Stated on the page because a cockpit that does
    /// not say how old it is invites being read as live when it is a minute stale.
    /// </summary>
    public DateTime EvaluatedAtUtc { get; init; }

    /// <summary>
    /// One line per signal, severity-ordered. Each carries the admin route that owns its
    /// detail — this payload deliberately holds no depth of its own.
    /// </summary>
    public IReadOnlyList<PipelineHealthSignalReadModel> Signals { get; init; } = [];

    public IReadOnlyList<ProcessHealthReadModel> Processes { get; init; } = [];

    public RawDataFreshnessReadModel RawData { get; init; } = new();

    public PipelineGapReadModel Gaps { get; init; } = new();
}

/// <summary>
/// One cockpit signal reduced to a verdict, a sentence, and where to go for the detail.
/// </summary>
public sealed record PipelineHealthSignalReadModel
{
    /// <summary>
    /// Stable identifier: <c>processes</c>, <c>dataQuality</c>, <c>ingestionLag</c>,
    /// <c>diskForecast</c>.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary><c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c>.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>The one sentence explaining this signal's current state.</summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>
    /// Why the signal could not be measured. Set <b>iff</b> <see cref="Status"/> is
    /// <c>unknown</c> — the tile renders this in place instead of a zero that would look
    /// healthy. A degraded sub-signal must explain itself, never fail the page.
    /// </summary>
    public string? UnknownReason { get; init; }

    /// <summary>
    /// The admin route owning this signal's depth (<c>/processes</c>,
    /// <c>/data-quality</c>, <c>/database</c>). Every tile is a link.
    /// </summary>
    public string DetailPath { get; init; } = string.Empty;
}

public sealed record ProcessHealthReadModel
{
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>
    /// Effective status, PascalCase: <c>Success</c> | <c>Failed</c> | <c>Running</c> |
    /// <c>Abandoned</c> | <c>Missing</c>.
    ///
    /// <para>
    /// Effective, not stored: a <c>Running</c> row whose heartbeat has gone stale reads
    /// <c>Abandoned</c> here, via the same <c>ProcessRunStaleness</c> policy
    /// <c>/ops/process-runs</c> applies. Before #1031 this endpoint skipped that step and
    /// lower-cased the name, so a dead run read <c>"running"</c> on the cockpit and
    /// <c>"Abandoned"</c> on the panel the cockpit links to.
    /// </para>
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Null when the process has never recorded a run (<c>Missing</c>).</summary>
    public DateTime? LastStartedAtUtc { get; init; }

    /// <inheritdoc cref="LastStartedAtUtc"/>
    public DateTime? LastFinishedAtUtc { get; init; }

    /// <summary>
    /// Last successful finish, all-time. Null when the process has never succeeded — which
    /// is a different answer from "succeeded long ago" and must not collapse into one.
    /// </summary>
    public DateTime? LastSuccessAtUtc { get; init; }

    /// <summary>
    /// Terminal runs since the last success, i.e. the current failure streak. 0 when the
    /// latest run succeeded.
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    public int DurationMs { get; init; }

    public string? Error { get; init; }
}

public sealed record RawDataFreshnessReadModel
{
    public int QueueId { get; init; }

    public int RawMatchCount { get; init; }

    public int RawParticipantCount { get; init; }

    public IReadOnlyList<PlatformRawDataFreshnessReadModel> Platforms { get; init; } = [];
}

public sealed record PlatformRawDataFreshnessReadModel
{
    public string PlatformId { get; init; } = string.Empty;

    public DateTime? LatestMatchStartAtUtc { get; init; }

    public string LatestPatchVersion { get; init; } = string.Empty;
}

public sealed record PipelineGapReadModel
{
    public double? MatchIngestionToMainAnalysisMinutes { get; init; }

    /// <summary>
    /// Minutes between the newest scoped match and the newest <c>main_champion_stats</c>
    /// computation. Null when either side has nothing to measure — notably when no scoped
    /// match exists at all, which before #1031 produced a vast negative number because the
    /// missing timestamp defaulted to <c>0001-01-01</c> instead of null.
    /// </summary>
    public double? ChampionDataLagMinutes { get; init; }
}
