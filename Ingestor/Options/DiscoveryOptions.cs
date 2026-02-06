namespace Ingestor.Options;

public class DiscoveryOptions
{
    public List<string> Platforms { get; set; } = new() { "KR", "EUW1", "NA1" };

    public List<string> TierScope { get; set; } = new() { "Master", "GM", "Challenger" };

    public int TopChampionsPerAccount { get; set; } = 10;

    public int MaxLastPlayDays { get; set; } = 10;

    public int MaxAccountsPerPlatformPerRun { get; set; } = 500;
}
