namespace Ingestor.Options;

public class ScoringOptions
{
    public int TopNPerPlatform { get; set; } = 200;

    public int MaxLastPlayDays { get; set; } = 10;

    public int TopChampionsPerAccount { get; set; } = 10;
}
