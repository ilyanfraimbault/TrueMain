namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The candidate funnel's <em>level</em> over time (#1403): how many rows sat in each
/// <c>MainCandidateStatus</c> at the end of each period, from the hourly snapshots the
/// ingestor records.
///
/// <para>
/// The companion of <see cref="CandidateFunnelReadModel"/>, which measures the same
/// funnel's <em>flow</em>. Neither can be derived from the other: a period that scored
/// 5,000 candidates and promoted 5,000 out of the pool leaves the level flat, and a
/// flat level is exactly what a stalled pipeline also produces. Flow says how much
/// moved, stock says how much is waiting.
/// </para>
///
/// <para>
/// <b>Forward-only, and never backfilled.</b> The series starts at the first recorded
/// snapshot (<see cref="EarliestSnapshotAtUtc"/>) and nothing before it is filled with
/// zeros — the level then was not zero, it was unmeasured (#924). Reconstructing it
/// afterwards is impossible in principle, not merely unimplemented: <c>main_candidates</c>
/// has no <c>QueuedAtUtc</c>, so Scored and Queued cannot be told apart in the past, and
/// pruning and demotion delete rows, so every past level would be understated by
/// whatever has since been removed.
/// </para>
/// </summary>
public sealed record CandidateStockReadModel
{
    /// <summary>One point per period, oldest first, empty before the first snapshot.</summary>
    public IReadOnlyList<CandidateStockBucket> Buckets { get; init; } = [];

    /// <summary>The requested window, after clamping.</summary>
    public int WindowDays { get; init; }

    /// <summary>
    /// The snapshot collection's TTL in days, so the panel can say why a 90-day window
    /// came back holding less than 90 days of curve.
    /// </summary>
    public int RetentionDays { get; init; }

    /// <summary>
    /// The oldest snapshot in the window, or null when there is none. The series
    /// genuinely begins here rather than at the window's left edge.
    /// </summary>
    public DateTime? EarliestSnapshotAtUtc { get; init; }

    /// <summary>
    /// Wall-clock time of the most recent reading, for the panel's "as of" line. Null
    /// when the step has never run.
    /// </summary>
    public DateTime? LatestSnapshotAtUtc { get; init; }
}

/// <summary>
/// One period's level, summed across platforms.
///
/// <para>
/// <b>Sampled, never summed across time.</b> A period holding several hourly readings
/// reports its <em>last</em> one: a stock is a level, and adding two readings of the
/// same 419,000 queued candidates would report 838,000 of them. Across platforms within
/// that one reading the counts <em>are</em> summed — those are disjoint populations at a
/// single instant.
/// </para>
///
/// <para>
/// Every status is present on every bucket, including the ones that read 0. A recorded
/// zero is a measurement — <c>New</c> at 0 means scoring drained its backlog, which is
/// the healthy state — and the read side must be able to tell it from an unmeasured
/// period, which is absent from <see cref="CandidateStockReadModel.Buckets"/> entirely.
/// </para>
/// </summary>
public sealed record CandidateStockBucket(
    string Bucket,
    long New,
    long Scored,
    long Queued,
    long Processing,
    long Validated,
    long Rejected,
    DateTime SampledAtUtc);
