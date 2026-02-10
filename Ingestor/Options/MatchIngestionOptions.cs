namespace Ingestor.Options;

public class MatchIngestionOptions
{
    public int BatchSize { get; set; } = 50;

    public int MatchesPerAccount { get; set; } = 20;

    public int SaveBatchSizeMatches { get; set; } = 10;

    public int ClaimLeaseMinutes { get; set; } = 30;

    public List<string> Platforms { get; set; } = new() { "KR", "EUW1", "NA1" };
}
