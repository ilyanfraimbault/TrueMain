namespace Ingestor.Options;

public class ScoringOptions
{
    public const string SectionName = "Scoring";

    public int TopNPerPlatform { get; set; } = 200;

    public int MaxLastPlayDays { get; set; } = 10;

    public int TopChampionsPerAccount { get; set; } = 10;

    public int BatchSize { get; set; } = 5000;

    public double RecencyWeight { get; set; } = 0.65;

    public double RankWeight { get; set; } = 0.20;

    public double PointsWeight { get; set; } = 0.15;

    /// <summary>
    /// Weight of the per-champion scarcity term in candidate scoring. Pulls candidates
    /// maining under-covered champions up the ranking so they cross the top-N ingestion
    /// queue, without changing the IsMain definition. Set to 0 to disable the bonus.
    /// </summary>
    public double ScarcityWeight { get; set; } = 0.25;

    /// <summary>
    /// Log10 normalizer for the observed-games merit of harvested candidates (#485),
    /// which have no mastery rank/points. With the default 1.5, ~30 observed games
    /// reaches a full merit score and the harvest threshold of 5 games scores ~0.5.
    /// Harvested candidates reuse the combined rank+points weight as their merit weight,
    /// so they stay on the same 0-100 scale as ladder candidates in the top-N.
    /// </summary>
    public double HarvestObservedGamesLogNormalizer { get; set; } = 1.5;
}
