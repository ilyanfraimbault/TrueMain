namespace Ingestor.Options;

public class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    /// <summary>
    /// Optional narrowing override of the shared <c>Platforms:Active</c> list (#496). Left empty
    /// — the default — ladder discovery inherits <see cref="PlatformScopeOptions.Active"/>, so a
    /// region is added in one place instead of three. An override must be a subset of the shared
    /// list; that is enforced at startup by <see cref="PlatformScopeValidator"/>.
    /// </summary>
    public List<string> Platforms { get; set; } = [];

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

    /// <summary>
    /// Minimum wall-clock gap between ladder discovery runs (#487). When the last
    /// completed Discovery run is more recent than this, the run is skipped so its Riot
    /// budget is reallocated to match ingestion — the participant harvest (#485) is the
    /// primary candidate source now, leaving the ladder crawl as a slow exploration arm.
    /// <see cref="TimeSpan.Zero"/> (default) runs it every iteration (legacy behaviour).
    /// </summary>
    public TimeSpan MinRunInterval { get; set; } = TimeSpan.Zero;
}
