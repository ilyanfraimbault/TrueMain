namespace Ingestor.Options;

public class MatchIngestionOptions
{
    public const string SectionName = "MatchIngestion";

    public int BatchSize { get; set; } = 50;

    public int MatchesPerAccount { get; set; } = 20;

    public int SaveBatchSizeMatches { get; set; } = 10;

    public int MaxMatchFetchConcurrency { get; set; } = 4;

    public int ClaimLeaseMinutes { get; set; } = 30;

    public List<string> Platforms { get; set; } = new() { "KR", "EUW1", "NA1" };
}
