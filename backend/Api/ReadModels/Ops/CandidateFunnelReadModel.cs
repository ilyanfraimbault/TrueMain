namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Candidate-pipeline throughput over time (#1024): how many candidates entered, were
/// promoted, cleared ingestion and were demoted per period — as opposed to the
/// instantaneous status counts the rest of <c>/candidates</c> shows. A funnel can be
/// full and completely stalled, and the two look identical on a status list.
/// </summary>
/// <remarks>
/// <para>
/// The source is the recorded process-run summaries in Mongo, never
/// <c>main_candidates</c> row counts. Retention prunes stale candidates
/// (<c>MatchDataRetentionProcess</c>), so counting rows by status per past period
/// under-reports every bucket, and increasingly so the further back you look. A run
/// summary is written once and never rewritten: deleting the candidate does not
/// un-count the run that discovered it.
/// </para>
/// <para>
/// Because of that source, the whole series is bounded by the <c>process_runs</c> TTL
/// (<see cref="RetentionDays"/>). Beyond it there are no runs to read, which is not the
/// same as a period where nothing happened — see <see cref="EarliestRunAtUtc"/>.
/// </para>
/// </remarks>
public sealed record CandidateFunnelReadModel
{
    /// <summary>
    /// The series, oldest bucket first, contiguous from the oldest run on record to the
    /// current period. Quiet periods inside that range are real zeros: runs happened,
    /// they just moved no candidate.
    /// </summary>
    public IReadOnlyList<CandidateFunnelBucket> Buckets { get; init; } = [];

    /// <summary>The window the caller asked for, in days, after clamping.</summary>
    public int WindowDays { get; init; }

    /// <summary>
    /// How long <c>process_runs</c> keeps a run. A <see cref="WindowDays"/> larger than
    /// this cannot be answered — the panel states the bound rather than drawing the
    /// missing tail as zero throughput.
    /// </summary>
    public int RetentionDays { get; init; }

    /// <summary>
    /// Start of the oldest run in the window, i.e. the earliest period this series can
    /// speak for. Null when no run survives in the window at all — an empty range, not a
    /// range of zeros.
    /// </summary>
    public DateTime? EarliestRunAtUtc { get; init; }

    /// <summary>
    /// Start of the first run whose summary carried the validated counter, which the
    /// ingestor only began recording with #1024. Buckets entirely before it have a null
    /// <see cref="CandidateFunnelBucket.Validated"/>: the pipeline was validating
    /// accounts then, nothing was counting them, and a health panel may not pass off what
    /// it did not measure as a measured zero (#924). Null when no run measured it yet.
    /// </summary>
    /// <remarks>
    /// The bucket that contains this instant is reported, not suppressed, even though it
    /// only covers the part of the period after the deploy — a partial first bucket is
    /// the normal shape of any forward-only counter.
    /// </remarks>
    public DateTime? ValidatedFirstMeasuredAtUtc { get; init; }
}

/// <summary>
/// One period of the funnel. Intake is split by the process that produced it because the
/// three sources fail independently: ladder discovery drying up and the orphan harvest
/// drying up are different incidents with the same total.
/// </summary>
/// <param name="Bucket">Period start, ISO-8601 UTC.</param>
/// <param name="IntakeLadder">
/// Candidates inserted by ladder discovery (<c>Discovery</c>, summed over platforms).
/// </param>
/// <param name="IntakeHarvest">
/// Candidates inserted by the orphan-participant harvest (<c>Harvest</c>).
/// </param>
/// <param name="IntakeManual">
/// Candidates queued by an operator's manual seed (<c>ManualSeed</c>). Queued, not
/// inserted: a manual seed promotes rows that discovery may already have created, so it
/// is intake into the <em>queue</em> rather than into the table.
/// </param>
/// <param name="Scored">Candidates scored by <c>Scoring</c>, summed over platforms.</param>
/// <param name="Promoted">
/// Candidates <c>Scoring</c> queued for ingestion — the per-platform top-N. The gap
/// against <paramref name="Scored"/> is the competitive cut, not a failure.
/// </param>
/// <param name="Validated">
/// Accounts whose candidates cleared ingestion (<c>MatchIngestion</c>). Null — not zero —
/// for periods before the counter existed; see
/// <see cref="CandidateFunnelReadModel.ValidatedFirstMeasuredAtUtc"/>.
/// </param>
/// <param name="Demoted">
/// Accounts <c>MainAnalysis</c> demoted back out of Validated on a critical play rate.
/// This is the funnel's only negative outcome today: the <c>Rejected</c> status exists on
/// the entity but no process ever assigns it.
/// </param>
/// <param name="Runs">
/// Runs of any of the six contributing processes that started in this period. Zero
/// separates "the pipeline did not run" from "it ran and moved nothing" — the whole point
/// of the panel.
/// </param>
public sealed record CandidateFunnelBucket(
    string Bucket,
    long IntakeLadder,
    long IntakeHarvest,
    long IntakeManual,
    long Scored,
    long Promoted,
    long? Validated,
    long Demoted,
    int Runs);
