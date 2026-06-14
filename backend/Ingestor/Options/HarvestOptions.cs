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
    /// Platforms to harvest. Should mirror <c>MatchIngestion:Platforms</c> (the harvest can
    /// only see matches we ingest) and <c>Discovery:Platforms</c>. This list is duplicated
    /// per section by the existing convention, so adding a region in one place but not here
    /// silently skips it — keeping the lists consistent (or cross-validating them) is tracked
    /// in #496.
    /// </summary>
    public List<string> Platforms { get; set; } = new() { "KR", "EUW1", "NA1" };

    /// <summary>Queue to aggregate over. 420 = ranked solo, the main-detection queue.</summary>
    public int QueueId { get; set; } = 420;

    /// <summary>
    /// Anti-noise / anti-explosion gate: only emit a candidate for a (puuid, champion)
    /// with at least this many observed games. A single observed game is not signal.
    /// </summary>
    public int MinObservedGames { get; set; } = 5;

    /// <summary>
    /// Upper bound on harvested rows processed per run, to cap scan/work. The cap is a single
    /// global LIMIT applied to the cross-platform result ordered by observed games desc, so a
    /// high-traffic region (e.g. KR) can consume most of the quota and starve smaller regions
    /// on an imbalanced run. Acceptable for now; a per-platform quota is a future refinement
    /// tracked alongside the harvest-fairness follow-up (#495).
    /// </summary>
    public int MaxCandidatesPerRun { get; set; } = 5000;

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
