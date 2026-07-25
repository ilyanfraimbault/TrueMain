using Core.Lol.Map;

namespace Ingestor.Options;

/// <summary>
/// Options for the participant harvest (#485): turning orphan
/// <c>match_participants</c> rows into <see cref="Data.Entities.MainCandidate"/>s
/// at near-zero Riot API cost.
/// </summary>
public class HarvestOptions
{
    public const string SectionName = "Harvest";

    /// <summary>
    /// Optional narrowing override of the shared <c>Platforms:Active</c> list (#496). Left empty
    /// — the default — the harvest inherits <see cref="PlatformScopeOptions.Active"/>, so a region
    /// is added in one place instead of three. An override must be a subset of the shared list
    /// <em>and</em> of <c>MatchIngestion:Platforms</c> (the harvest can only see matches we
    /// ingest); both are enforced at startup by <see cref="PlatformScopeValidator"/>.
    /// </summary>
    public List<string> Platforms { get; set; } = [];

    /// <summary>Queue to aggregate over. Defaults to ranked solo, the main-detection queue.</summary>
    public int QueueId { get; set; } = (int)LolQueueId.RankedSoloDuo;

    /// <summary>
    /// Anti-noise / anti-explosion gate: only emit a candidate for a (puuid, champion)
    /// with at least this many observed games. A single observed game is not signal.
    /// </summary>
    public int MinObservedGames { get; set; } = 5;

    /// <summary>
    /// Upper bound on harvested rows processed per run, to cap scan/work. Split between
    /// brand-new (puuid, champion) pairs and refreshes of existing harvest candidates by
    /// <see cref="NewCandidateShare"/> (#495), so a saturated pool cannot spend the whole
    /// budget re-reading the players it already knows.
    ///
    /// The budget is still cross-platform: a high-traffic region (e.g. KR) can consume most
    /// of it and leave smaller regions behind on an imbalanced run. That bound is reported
    /// per platform on the <c>HarvestBudgetExhausted</c> ops event rather than fixed here;
    /// a per-platform quota remains a possible refinement.
    ///
    /// Raising this also raises the transient heap a run holds: it is the per-class,
    /// per-platform fetch cap too, so a run materialises up to
    /// <c>platforms x 2 x MaxCandidatesPerRun</c> rows (~8 MB at the defaults; see
    /// <c>ParticipantHarvestService.HarvestAsync</c> for the arithmetic).
    /// </summary>
    public int MaxCandidatesPerRun { get; set; } = 5000;

    /// <summary>
    /// Share of <see cref="MaxCandidatesPerRun"/> reserved for (puuid, champion) pairs that
    /// have no candidate yet, between 0 and 1. Without a reservation the run orders the whole
    /// eligible pool by observed games and cuts at the cap, which — once the pool outgrows the
    /// cap — permanently hands every slot to the most-observed players, all of them already
    /// candidates: a pair that just crossed <see cref="MinObservedGames"/> would never be
    /// harvested (#495).
    ///
    /// The reservation is a floor, not a partition: whatever one class cannot fill spills to
    /// the other, so a run always uses its full budget. The extremes are priorities, not
    /// filters — <c>0</c> serves refreshes first (the pre-#495 order) and <c>1</c> serves new
    /// pairs first, but in both cases the class served second still takes whatever budget the
    /// first one left unused. No value of this share disables a class: with <c>1</c>, a run
    /// whose new pairs do not fill the budget still spends the remainder on refreshes.
    /// </summary>
    public double NewCandidateShare { get; set; } = 0.5;

    /// <summary>
    /// Only aggregate participant rows from matches started within this many days. Bounds the
    /// scan explicitly rather than relying on <c>MatchDataRetention</c> having physically
    /// deleted older rows, and focuses the harvest on currently-active players. Should roughly
    /// cover the retained window (~2 patches). <c>0</c> disables the date filter (scan all).
    /// </summary>
    public int LookbackDays { get; set; } = 30;

    /// <summary>Pending changes flushed to the DB per batch while upserting.</summary>
    public int SaveBatchSize { get; set; } = 200;
}
