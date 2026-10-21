using System.ComponentModel.DataAnnotations;

namespace Ingestor.Options;

/// <summary>
/// Batch sizing for <c>ChampionScalingAggregationProcess</c> (#1368). Lighter per match than
/// its siblings — one row per cohort member, no pairing — so it takes larger batches and a
/// higher per-run cap: the table ships empty and the scaling panel reads it from day one, so
/// the sooner the retained history drains, the sooner the panel counts every game again.
/// </summary>
public class ScalingAggregationOptions
{
    public const string SectionName = "ScalingAggregation";

    /// <summary>Matches per transaction.</summary>
    [Range(1, int.MaxValue)]
    public int MatchBatchSize { get; set; } = 1000;

    /// <summary>Most matches folded per run; 0 = drain the whole backlog.</summary>
    [Range(0, int.MaxValue)]
    public int MaxMatchesPerRun { get; set; } = 100000;
}
