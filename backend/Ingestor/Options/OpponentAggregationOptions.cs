using System.ComponentModel.DataAnnotations;

namespace Ingestor.Options;

/// <summary>
/// Batch sizing for <c>ChampionOpponentAggregationProcess</c> (#1713). Same shape and
/// defaults as <see cref="SynergyAggregationOptions"/>: the fold reads the same
/// participant rows and emits five opposing pairs per tracked seat where the synergy
/// fold emits four.
/// </summary>
public class OpponentAggregationOptions
{
    public const string SectionName = "OpponentAggregation";

    /// <summary>Matches per transaction.</summary>
    [Range(1, int.MaxValue)]
    public int MatchBatchSize { get; set; } = 500;

    /// <summary>Most matches folded per run; 0 = drain the whole backlog.</summary>
    [Range(0, int.MaxValue)]
    public int MaxMatchesPerRun { get; set; } = 20000;
}
