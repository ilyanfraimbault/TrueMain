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
    /// When true (#486), ladder discovery slides a per-platform window across the
    /// ladder over successive runs (persisted offset cursor) instead of always
    /// re-scanning the top <see cref="MaxAccountsPerPlatformPerRun"/> entries — which
    /// is a large part of why <c>newAccounts</c> ≈ 0 on a saturated ladder. The window
    /// size is <see cref="MaxAccountsPerPlatformPerRun"/>; the offset advances by the
    /// window each run and wraps at the end of the ladder. Set false to restore the
    /// always-top-of-ladder behaviour.
    /// </summary>
    public bool SlidingWindowEnabled { get; set; } = true;
}
