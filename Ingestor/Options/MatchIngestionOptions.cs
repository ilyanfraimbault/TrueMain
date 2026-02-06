namespace Ingestor.Options;

public class MatchIngestionOptions
{
    public int BatchSize { get; set; } = 50;

    public int MatchesPerAccount { get; set; } = 20;

    public List<string> Platforms { get; set; } = new() { "KR", "EUW1", "NA1" };
}
