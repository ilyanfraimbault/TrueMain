namespace TrueMain.ReadModels.Ops;

/// <summary>
/// How long candidates currently in <c>main_candidates</c> took to move through the
/// queue (#1024): discovery → scoring, then scoring → validated.
/// </summary>
/// <remarks>
/// <para>
/// This is a <strong>snapshot over retained rows</strong>, not a historical series, and
/// must be labelled as one wherever it is shown. It is computed from the timestamps on
/// the candidates that still exist right now, so it says nothing about candidates
/// retention has since pruned, and a period cannot be selected — the same row
/// contributes to it whenever it is asked.
/// </para>
/// <para>
/// The bias that follows is worth stating: pruning removes stale never-promoted
/// candidates, so the surviving population skews towards the ones that did move. Read the
/// numbers as "how fast the queue serves what is in it", not as "how long a candidate
/// waits".
/// </para>
/// </remarks>
public sealed record CandidateQueueLatencyReadModel
{
    /// <summary>
    /// Discovery to scoring, over candidates that have been scored. Measured from
    /// <c>DiscoveredAtUtc</c> to <c>ScoredAtUtc</c>.
    /// </summary>
    public CandidateLatencyLeg DiscoveredToScored { get; init; } = CandidateLatencyLeg.Empty;

    /// <summary>
    /// Scoring to clearing ingestion, over candidates that have been validated. Measured
    /// from <c>ScoredAtUtc</c> to <c>ValidatedAtUtc</c>.
    /// </summary>
    /// <remarks>
    /// <c>ValidatedAtUtc</c> was only written from #1024 onwards — the promotion used to
    /// set the status alone — so this leg starts empty on deploy and fills as accounts
    /// are validated. An empty leg here means "not measured yet", which is why
    /// <see cref="CandidateLatencyLeg.Samples"/> is reported next to the percentiles.
    /// </remarks>
    public CandidateLatencyLeg ScoredToValidated { get; init; } = CandidateLatencyLeg.Empty;

    /// <summary>
    /// Candidate rows currently retained — the population every leg above is drawn from,
    /// so a reader can see how much of it each leg actually covers.
    /// </summary>
    public long RetainedCandidates { get; init; }

    /// <summary>When the snapshot was taken.</summary>
    public DateTime AsOfUtc { get; init; }
}

/// <summary>
/// One leg of the queue, as a median and a p90 in seconds. Both are null when
/// <paramref name="Samples"/> is 0: no row carried both timestamps, which is not a
/// latency of zero.
/// </summary>
/// <param name="Samples">Candidate rows that carried both ends of the leg.</param>
/// <param name="MedianSeconds">The typical wait.</param>
/// <param name="P90Seconds">
/// The slow tail. Reported alongside the median because the two diverging is the shape a
/// backed-up queue has, and a median alone hides it.
/// </param>
public sealed record CandidateLatencyLeg(long Samples, double? MedianSeconds, double? P90Seconds)
{
    public static CandidateLatencyLeg Empty { get; } = new(0, null, null);
}
