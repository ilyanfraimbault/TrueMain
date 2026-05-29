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
}
