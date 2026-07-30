namespace TrueMain.Options;

/// <summary>
/// Where every green/amber/red line on the data-quality detector panel is drawn (#924),
/// bound from <c>DataQualityDetectors:*</c>.
///
/// <para>
/// These are all judgement calls — "how stale is too stale", "how orphaned is
/// abnormal" — and judgement calls belong in configuration, not in a
/// <c>const int</c>: the honest value differs between preprod (tiny, 1-patch
/// retention, ingestion in bursts) and production, and an operator who wants to
/// stop a card crying wolf must be able to move the line without a redeploy. Same
/// reasoning, and the same binding shape, as <c>StorageHistoryOptions</c> (#925).
/// </para>
///
/// <para>
/// <b>Convention for every threshold pair:</b> the value is the level at which the
/// status is reached (<c>&gt;=</c> for "more is worse" counts and ages), and a value
/// of <c>0</c> or less <b>disables</b> that level — the detector can then never reach
/// it. That is how a warning-only signal is expressed (amber set, red disabled)
/// without a second flag per detector.
/// </para>
/// </summary>
public sealed class DataQualityDetectorOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DataQualityDetectors";

    // ---- duplicate dimension rows (#911's class) -----------------------------

    /// <summary>
    /// Canonical-key groups holding more than one <c>champion_dim_*</c> row before the
    /// card goes amber. Defaults to 1: a single permutation duplicate is already a split
    /// sample, so there is no benign band here — the amber level exists only so an
    /// operator draining a known backlog can raise it temporarily.
    /// </summary>
    public long DuplicateDimensionGroupsAmber { get; set; } = 1;

    /// <summary>Duplicate groups before the card goes red. Defaults to 1 — see above.</summary>
    public long DuplicateDimensionGroupsRed { get; set; } = 1;

    /// <summary>
    /// Rows stored outside canonical order before the card goes amber. They are not
    /// duplicates yet; left alone the reader's canonical lookup misses them and mints a
    /// second row, which is how #911 would come back.
    /// </summary>
    public long NonCanonicalDimensionRowsAmber { get; set; } = 1;

    /// <summary>
    /// Non-canonical rows before the card goes red. Disabled (0) by default: the
    /// duplicate count is the corruption signal, this one is the early warning.
    /// </summary>
    public long NonCanonicalDimensionRowsRed { get; set; }

    // ---- aggregate freshness -------------------------------------------------

    /// <summary>
    /// Hours since an aggregation last <em>succeeded</em> before it reads as amber. The
    /// pipeline runs back-to-back many times a day (<c>RunOnce</c> +
    /// <c>restart: unless-stopped</c>), so a fold that has not completed in six hours has
    /// either failed repeatedly or is starved behind a longer step.
    /// </summary>
    public double AggregationStaleAmberHours { get; set; } = 6;

    /// <summary>Hours since an aggregation last succeeded before it reads as red.</summary>
    public double AggregationStaleRedHours { get; set; } = 24;

    /// <summary>
    /// Champions to list, stalest first, in the on-demand per-champion freshness
    /// drill-down. The whole roster is about 170, so this is a display cap rather than a
    /// cost bound.
    /// </summary>
    public int FreshnessChampionLimit { get; set; } = 40;

    /// <summary>
    /// How many patches (newest first) the per-champion freshness drill-down covers.
    /// Older patches are frozen by design (#466) and can never be refreshed, so
    /// reporting them as stale would be noise that never clears.
    /// </summary>
    public int FreshnessPatchCount { get; set; } = 2;

    // ---- orphan participants -------------------------------------------------

    /// <summary>
    /// Matches sampled per platform, newest first, for the orphan-ratio measurement. The
    /// whole-table ratio is a <c>COUNT(*)</c> over millions of <c>match_participants</c>
    /// rows and cannot run on a page view; the newest matches per platform are an index
    /// range on <c>IX_matches_platform_queue_game_start</c>, and they answer the question
    /// that actually matters — is <em>current</em> ingestion still attributing players to
    /// tracked accounts. Split in half to give the trend, so this is rounded down to an
    /// even number.
    /// </summary>
    public int OrphanSampleMatchesPerPlatform { get; set; } = 60;

    /// <summary>
    /// Orphan share of sampled participants, in percent, before the card goes amber.
    /// A high share is <em>normal</em>: a tracked player's game contributes one tracked
    /// row and nine untracked ones, so ~90% is the healthy resting state. The anomaly is
    /// the approach to 100%, where nothing is being attributed at all.
    /// </summary>
    public double OrphanRatioAmberPercent { get; set; } = 95;

    /// <summary>Orphan share, in percent, before the card goes red.</summary>
    public double OrphanRatioRedPercent { get; set; } = 99;

    /// <summary>
    /// Rise in the orphan share, in percentage points, between the older and newer half
    /// of the sample before the card goes amber. Catches a regression that has not yet
    /// pushed the absolute level past its own threshold.
    /// </summary>
    public double OrphanRatioRiseAmberPoints { get; set; } = 5;

    /// <summary>Rise in the orphan share, in percentage points, before the card goes red.</summary>
    public double OrphanRatioRiseRedPoints { get; set; } = 15;

    /// <summary>
    /// Hours since the Harvest step last succeeded before it reads as amber. Harvest is
    /// what turns orphan participants into candidates, so its silence is the other half of
    /// a rising orphan share.
    /// </summary>
    public double HarvestStaleAmberHours { get; set; } = 12;

    /// <summary>Hours since Harvest last succeeded before it reads as red.</summary>
    public double HarvestStaleRedHours { get; set; } = 48;

    // ---- ingestion lag -------------------------------------------------------

    /// <summary>
    /// Age of the newest ingested match on a platform, in hours, before it reads as amber.
    /// </summary>
    public double IngestionLagAmberHours { get; set; } = 6;

    /// <summary>Age of the newest ingested match on a platform, in hours, before it reads as red.</summary>
    public double IngestionLagRedHours { get; set; } = 24;

    /// <summary>Matches awaiting a timeline before the queue-depth row reads as amber.</summary>
    public long PendingTimelineAmber { get; set; } = 5_000;

    /// <summary>Matches awaiting a timeline before the queue-depth row reads as red.</summary>
    public long PendingTimelineRed { get; set; } = 25_000;

    /// <summary>Candidates sitting in <c>Queued</c> before the queue-depth row reads as amber.</summary>
    public long QueuedCandidatesAmber { get; set; } = 20_000;

    /// <summary>Candidates sitting in <c>Queued</c> before the queue-depth row reads as red.</summary>
    public long QueuedCandidatesRed { get; set; } = 100_000;

    /// <summary>
    /// Candidates stuck in <c>Processing</c> before the queue-depth row reads as amber.
    /// <c>Processing</c> is a lease state, so a large standing population means leases are
    /// leaking rather than that work is queued.
    /// </summary>
    public long ProcessingCandidatesAmber { get; set; } = 500;

    /// <summary>Candidates stuck in <c>Processing</c> before the queue-depth row reads as red.</summary>
    public long ProcessingCandidatesRed { get; set; } = 5_000;

    // ---- row-level sanity ----------------------------------------------------

    /// <summary>
    /// Impossible aggregate rows (more wins than games, more decided lanes than lanes,
    /// bans above their own denominator) before the card goes amber. Defaults to 1: these
    /// cannot happen by degree — one is a fold bug.
    /// </summary>
    public long InconsistentAggregateRowsAmber { get; set; } = 1;

    /// <summary>Impossible aggregate rows before the card goes red.</summary>
    public long InconsistentAggregateRowsRed { get; set; } = 1;

    /// <summary>
    /// Zero-sample aggregate rows before the card goes amber. Harmless to a reader (a
    /// sample floor hides them) but still a row that should never have been written.
    /// </summary>
    public long ZeroSampleAggregateRowsAmber { get; set; } = 1;

    /// <summary>
    /// Zero-sample aggregate rows before the card goes red. Disabled (0) by default —
    /// they distort nothing, unlike an impossible row.
    /// </summary>
    public long ZeroSampleAggregateRowsRed { get; set; }

    /// <summary>
    /// A patch whose match count falls below this fraction of the median comparable patch
    /// is flagged as abnormally thin. Judgement, not statistics: ingestion volume does
    /// swing with a patch's length and with how much of it we were up for.
    /// </summary>
    public double PatchVolumeAnomalyRatio { get; set; } = 0.4;

    /// <summary>
    /// Comparable patches required before the volume check is attempted at all. Below it
    /// there is no median worth dividing by and the check reports unknown rather than
    /// green. Note that the newest and the oldest retained patch are never <em>judged</em>
    /// (one is still filling, the other is being retention-trimmed), so a corpus needs
    /// this many patches on top of those two before the check can fire.
    /// </summary>
    public int PatchVolumeMinPatches { get; set; } = 3;
}
