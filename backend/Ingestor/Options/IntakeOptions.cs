namespace Ingestor.Options;

/// <summary>
/// Sizes the candidate intake to what the match-ingest claim can actually absorb (#1361).
///
/// <para>
/// Every stage before the claim used to carry its own absolute budget, and none of them was
/// derived from the one number that decides how many new accounts a cycle can consume:
/// <c>MatchIngestion:BatchSize x (1 - MatchIngestion:EstablishedMainShare)</c>. Measured in
/// production on 2026-09-02 that is ~22 accounts per cycle, against a promotion of up to 900
/// (<c>Scoring:TopNPerPlatform</c> 300 x 3 platforms) and a harvest that refreshed ~7 450
/// candidate rows every run. The result was a queue of 773 k <c>Queued</c> candidates whose
/// head never moved, rewritten constantly by scores nobody would read before they went stale.
/// </para>
///
/// <para>
/// The knobs here do not replace the existing ones — <c>Scoring:TopNPerPlatform</c> stays the
/// explicit ceiling, this section adds the dynamic cap underneath it — so a stage can still be
/// clamped by hand without reasoning about the derivation.
/// </para>
/// </summary>
public class IntakeOptions
{
    public const string SectionName = "Intake";

    /// <summary>
    /// How many cycles of claim capacity the queue is allowed to hold ahead of itself.
    /// The per-platform promotion cap is
    /// <c>claim capacity x this / platform count</c>: at 3, the queue carries roughly three
    /// cycles of work, enough that a cycle is never starved by a platform whose scored pool
    /// happens to be short, and far from the ~40x over-supply it replaces.
    /// </summary>
    public double PromotionHeadroomFactor { get; set; } = 3;

    /// <summary>
    /// Floor under the derived promotion cap. The derivation collapses towards zero on a small
    /// claim batch (a preprod-sized configuration, or an operator temporarily shrinking
    /// <c>MatchIngestion:BatchSize</c>), and a promotion of zero would stall the funnel
    /// entirely rather than merely slow it. The floor never raises the cap above
    /// <c>Scoring:TopNPerPlatform</c>, which stays the hard ceiling.
    /// </summary>
    public int MinPromotionPerPlatform { get; set; } = 25;

    /// <summary>
    /// Queue-depth cap per platform. Above it, retention demotes the lowest-scored excess
    /// <c>Queued</c> candidates back to <c>Scored</c> — never deletes them (#900's "deactivate,
    /// never delete" reasoning applies: a demoted candidate is re-scored and can be promoted
    /// again, and the row is the only record that the player was ever seen).
    /// <c>0</c> disables the drain.
    /// </summary>
    public int MaxQueuedPerPlatform { get; set; } = 5000;

    /// <summary>
    /// Rows demoted per <c>ExecuteUpdate</c> statement. Explicit and small so a one-off drain
    /// of a ~700 k-row backlog cannot blow the 300 s command timeout — the same
    /// incremental-progress reasoning as the retention delete batches (#988).
    /// </summary>
    public int QueueDepthDemotionBatchSize { get; set; } = 5000;

    /// <summary>
    /// Statements per platform per retention run. Bounds how much of the backlog one run
    /// drains, so the drain is spread over cycles instead of monopolising a run:
    /// <c>QueueDepthDemotionBatchSize x this</c> rows per platform per run.
    /// </summary>
    public int MaxDemotionBatchesPerRun { get; set; } = 4;

    /// <summary>
    /// How far the claim's <c>MatchIngestion:EstablishedMainShare</c> may swing either side of
    /// its configured value in response to the coverage deficit the allocator already computes
    /// (#1150). The configured value stays the midpoint: a platform mix at half the target
    /// deficit claims exactly the configured share, a fully covered one shifts
    /// <em>up</em> by this much (depth over breadth — more games from the mains we have), and
    /// one far below target shifts <em>down</em> by this much (breadth first — more of the
    /// batch goes to new candidates). <c>0</c> restores the fixed share.
    /// </summary>
    public double EstablishedMainShareSwing { get; set; } = 0.2;
}
