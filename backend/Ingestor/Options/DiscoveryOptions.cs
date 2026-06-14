namespace Ingestor.Options;

public class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    public List<string> Platforms { get; set; } = new() { "KR", "EUW1", "NA1" };

    public List<string> TierScope { get; set; } = new() { "Master", "GM", "Challenger" };

    public int TopChampionsPerAccount { get; set; } = 10;

    public int MaxLastPlayDays { get; set; } = 10;

    public int MaxAccountsPerPlatformPerRun { get; set; } = 350;

    public int NewAccountsTarget { get; set; } = 50;

    public int SaveBatchSize { get; set; } = 50;

    /// <summary>
    /// Minimum wall-clock gap between ladder discovery runs (#487). When the last
    /// completed Discovery run is more recent than this, the run is skipped so its Riot
    /// budget is reallocated to match ingestion — the participant harvest (#485) is the
    /// primary candidate source now, leaving the ladder crawl as a slow exploration arm.
    /// <see cref="TimeSpan.Zero"/> (default) runs it every iteration (legacy behaviour).
    /// </summary>
    public TimeSpan MinRunInterval { get; set; } = TimeSpan.Zero;
}
