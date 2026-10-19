using System.ComponentModel.DataAnnotations;

namespace Ingestor.Options;

/// <summary>
/// Sizing and the rebuild switch for <c>ChampionPatternAggregationProcess</c> (#1367).
/// </summary>
public class PatternAggregationOptions
{
    public const string SectionName = "PatternAggregation";

    /// <summary>
    /// Accounts whose slices are rebuilt per transaction. The working set is every
    /// retained game those accounts played on the champions being rebuilt, item and
    /// skill timelines included — the column that once grew the heap to ~6 GB when the
    /// aggregation read a whole patch at once (#601). At a few dozen games per slice,
    /// 500 accounts stays in the tens of thousands of rows.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int AccountsPerBatch { get; set; } = 500;

    /// <summary>
    /// Pending matches handled per step: their slices are rebuilt, then they are flagged. It
    /// is what a run that is cut short keeps — the first run after the flag ships rebuilds the
    /// whole window, which takes hours on preprod and gets restarted by every deploy, so it
    /// has to make progress a step at a time rather than only once it is over.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MatchesPerStep { get; set; } = 2000;

    /// <summary>
    /// Sends every match of the retained window back to the aggregation at the start of
    /// the run, so every live slice is rebuilt. For the day the slice shape or a
    /// dimension changes (#911-style repairs) or <c>MainAnalysis:AggregateNonMainPopulation</c>
    /// is flipped; set it for one run and take it off again — left on, every run costs
    /// the full rebuild the incremental fold exists to avoid.
    /// </summary>
    public bool RebuildAll { get; set; }
}
