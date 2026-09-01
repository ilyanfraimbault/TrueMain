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

    /// <summary>
    /// Apex ladders to crawl: any of <c>Master</c>, <c>GM</c> (or <c>Grandmaster</c>) and
    /// <c>Challenger</c> — the only tiers league-v4 exposes a dedicated ladder endpoint
    /// for.
    /// <para>
    /// Deliberately empty by default, with the shipped value living in
    /// <c>appsettings.json</c> (#860): <see cref="Microsoft.Extensions.Configuration.ConfigurationBinder"/>
    /// <em>appends</em> bound entries to a list that already has items, so a hard-coded
    /// default here would have survived — and been silently unioned into — any narrower
    /// scope an operator configures, the same bug <see cref="PlatformScopeOptions.Active"/>
    /// had before #496/#854. An empty list fails startup validation with an explicit
    /// message instead.
    /// </para>
    /// </summary>
    public List<string> TierScope { get; set; } = [];

    public int TopChampionsPerAccount { get; set; } = 10;

    public int MaxLastPlayDays { get; set; } = 10;

    /// <summary>
    /// Ladder entries scanned per platform per run — also the width of the sliding window when
    /// <see cref="SlidingWindowEnabled"/> is on.
    /// <para>
    /// 500, not the 350 this property used to declare: <c>appsettings.json</c> carried a 500 that
    /// overrode it, so 500 is what every run without an explicit override has actually used. The
    /// duplicate key is gone (defaults live in the class, not in <c>appsettings.json</c>) and the
    /// value that was really applying moved here, rather than letting the dead one silently take
    /// over. Both deployed stacks override it regardless — 750 in prod, 100 in preprod.
    /// </para>
    /// </summary>
    public int MaxAccountsPerPlatformPerRun { get; set; } = 500;

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
