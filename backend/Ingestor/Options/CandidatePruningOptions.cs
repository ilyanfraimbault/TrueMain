namespace Ingestor.Options;

/// <summary>
/// Options for pruning stale, never-promoted <see cref="Data.Entities.MainCandidate"/>s
/// (#487). The participant harvest generates many low-quality candidates; without pruning
/// the table grows unbounded. Pruning runs as a step of <c>MatchDataRetentionProcess</c>.
/// </summary>
public class CandidatePruningOptions
{
    public const string SectionName = "CandidatePruning";

    /// <summary>Whether to prune at all. Disable to keep every candidate forever.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// A never-promoted candidate (New/Scored/Rejected, never validated) is pruned once its
    /// last activity (mastery last-play for ladder, last observed game for harvest) is older
    /// than this. Should exceed the match-retention window so an actively-observed player is
    /// not pruned only to be re-harvested next run.
    /// </summary>
    public int PruneAfterDays { get; set; } = 30;
}
