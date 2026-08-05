namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Match ingestion throughput over time (#1025) — how many matches the pipeline
/// actually ingested per period.
///
/// <para>
/// Deliberately <b>not</b> the same question as <c>/ops/stats/matches-over-time</c>,
/// which buckets <c>matches.GameStartTimeUtc</c> and therefore describes when the
/// games were <em>played</em>: a property of the player population, which barely
/// moves when ingestion stalls and grows in the *past* when a backfill lands. This
/// one buckets the ingestor's own runs, so it answers "did the pipeline keep up this
/// week" — the signal the two retention crash-loops (#982, #988) needed and nothing
/// in the portal carried.
/// </para>
///
/// <para>
/// Sourced from the recorded run summaries in Mongo rather than from
/// <c>matches.CreatedAtUtc</c>. That column exists and looks tempting, but retention
/// deletes out-of-window and non-tracked-queue matches, so an old bucket shrinks over
/// time and the curve rewrites its own history. Deleting a match does not rewrite the
/// run that ingested it.
/// </para>
/// </summary>
public sealed record MatchesIngestedReadModel
{
    /// <summary>
    /// One bucket per period, oldest first. Periods inside the observed range with no
    /// runs are present with zero counters — a stalled pipeline is exactly what this
    /// chart is for, so its gaps have to be visible rather than absent.
    /// </summary>
    public IReadOnlyList<MatchesIngestedBucket> Buckets { get; init; } = [];

    /// <summary>The effective window in days, after clamping the caller's request.</summary>
    public int WindowDays { get; init; }

    /// <summary>
    /// How far back run history can possibly go: the <c>process_runs</c> TTL, in days
    /// (180 by default). Reported so the panel can state the bound instead of drawing
    /// the empty tail beyond it as if the pipeline had ingested nothing then.
    /// </summary>
    public int RetentionDays { get; init; }

    /// <summary>
    /// Start of the oldest run actually seen, or <see langword="null"/> when the
    /// window holds none. Zero-filling starts here rather than at the window's edge:
    /// a period older than the oldest surviving run was not measured, and painting it
    /// as zero would claim an idle pipeline we have no record of.
    /// </summary>
    public DateTime? EarliestRunAtUtc { get; init; }
}

/// <summary>
/// One period's ingestion counters, summed over every run that started in it.
/// </summary>
/// <param name="Bucket">
/// ISO-8601 UTC timestamp of the period start (<c>yyyy-MM-ddTHH:mm:ssZ</c>), matching
/// the key shape <c>/ops/stats/matches-over-time</c> emits so the admin formats both
/// series with one helper. Weeks start on <b>Monday</b>, like Postgres'
/// <c>date_trunc('week')</c> — Mongo's <c>$dateTrunc</c> would default to Sunday and
/// the two charts would disagree by a day.
/// </param>
/// <param name="MatchesInserted">
/// Matches newly written in the period. The headline number.
/// </param>
/// <param name="MatchesSkipped">
/// Matches the pipeline saw and did not write (already ingested, or filtered out).
/// Carried because inserted-alone cannot distinguish "nothing to do" from "working
/// hard and storing nothing", and those are opposite operational states.
/// </param>
/// <param name="TimelinesUpdated">Timelines fetched and attached in the period.</param>
/// <param name="Runs">
/// How many ingestion runs started in the period, summary or not. A period with runs
/// but no inserts is a real signal; a period with no runs at all is a stopped
/// ingestor.
/// </param>
public sealed record MatchesIngestedBucket(
    string Bucket,
    long MatchesInserted,
    long MatchesSkipped,
    long TimelinesUpdated,
    int Runs);
