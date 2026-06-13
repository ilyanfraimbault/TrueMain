namespace Ingestor.Options;

/// <summary>
/// Options for the participant harvest (#485): turning orphan
/// <c>match_participants</c> rows into <see cref="Data.Entities.MainCandidate"/>s
/// at near-zero Riot API cost.
/// </summary>
public class HarvestOptions
{
    public const string SectionName = "Harvest";

    /// <summary>Platforms to harvest. Matches the platforms we ingest matches for.</summary>
    public List<string> Platforms { get; set; } = new() { "KR", "EUW1", "NA1" };

    /// <summary>Queue to aggregate over. 420 = ranked solo, the main-detection queue.</summary>
    public int QueueId { get; set; } = 420;

    /// <summary>
    /// Anti-noise / anti-explosion gate: only emit a candidate for a (puuid, champion)
    /// with at least this many observed games. A single observed game is not signal.
    /// </summary>
    public int MinObservedGames { get; set; } = 5;

    /// <summary>Upper bound on harvested rows processed per run, to cap scan/work.</summary>
    public int MaxCandidatesPerRun { get; set; } = 5000;

    /// <summary>Pending changes flushed to the DB per batch while upserting.</summary>
    public int SaveBatchSize { get; set; } = 200;
}
