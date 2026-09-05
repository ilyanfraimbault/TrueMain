namespace Ingestor.Options;

public class LadderSyncOptions
{
    public const string SectionName = "LadderSync";

    /// <summary>
    /// Optional narrowing override of the shared <c>Platforms:Active</c> list, with the same
    /// contract as <see cref="DiscoveryOptions.Platforms"/> (#496): empty inherits the shared
    /// list, and an override must be a subset of it (enforced at startup by
    /// <c>PlatformScopeValidator</c>).
    /// </summary>
    public List<string> Platforms { get; set; } = [];

    /// <summary>
    /// Tiers to keep in sync, in any order — the sweep always walks them highest-first.
    /// <para>
    /// Master, Grandmaster and Challenger are read through their dedicated league-v4 ladder
    /// endpoints: one call returns the whole tier, so they are refreshed on every run and do
    /// not draw on <see cref="MaxRequestsPerRun"/>. Every other tier is only reachable through
    /// the paginated per-division endpoint and is swept incrementally under that budget.
    /// </para>
    /// <para>
    /// Deliberately empty by default for the same reason as <see cref="DiscoveryOptions.TierScope"/>
    /// (#860): <c>ConfigurationBinder</c> <em>appends</em> to a non-empty list, so a hard-coded
    /// default here would be silently unioned into any narrower scope an operator configures.
    /// The shipped value lives in <c>appsettings.json</c>; an empty list fails startup validation.
    /// </para>
    /// </summary>
    public List<string> TierScope { get; set; } = [];

    /// <summary>
    /// Ceiling on the paginated calls one run may spend, shared across every platform. The apex
    /// ladders are not counted against it.
    /// <para>
    /// This is the knob that makes the sweep depth safe to configure: a full Challenger→Emerald
    /// pass over three platforms is on the order of 3 900 calls, so it can only ever be walked a
    /// slice at a time. The cursor makes each run resume where the previous one stopped, so the
    /// budget buys sweep <em>rate</em>, not sweep <em>coverage</em>. 0 disables the paginated
    /// sweep entirely and leaves only the apex refresh.
    /// </para>
    /// </summary>
    public int MaxRequestsPerRun { get; set; } = 300;

    /// <summary>
    /// Ceiling on the paginated calls spent per UTC day, across every run (#1474).
    /// <para>
    /// <see cref="MaxRequestsPerRun"/> alone bounds a run, not a day: the process runs on every
    /// pipeline iteration, so its daily cost was <c>budget × iterations</c> — a number nobody
    /// chose, and one that grew with loop speed. Measured against a ladder where ~95 % of the
    /// accounts matched come back unchanged, the extra sweeps bought nothing. A run takes the
    /// smaller of the per-run cap and what is left of the day; 0 disables the daily ceiling.
    /// </para>
    /// </summary>
    public int MaxRequestsPerDay { get; set; }

    /// <summary>
    /// Minimum interval between two ladder syncs (#1474), with the same contract as
    /// <see cref="DiscoveryOptions.MinRunInterval"/>: measured from the last run that actually
    /// did its work, and <see cref="TimeSpan.Zero"/> (default) runs it every iteration.
    /// <para>
    /// A ladder moves slowly; re-reading it on every iteration serialises the process against a
    /// single platform host's rate limit for wall-clock time the fetch lane would otherwise
    /// spend ingesting matches.
    /// </para>
    /// </summary>
    public TimeSpan MinRunInterval { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Minimum interval between two full re-reads of the apex ladders (#1474). One call returns a
    /// whole tier, so the Riot cost is negligible — but Master alone is tens of thousands of
    /// entries to join against <c>riot_accounts</c> on every run, for a few hundred changes.
    /// Measured from the last run that refreshed them; <see cref="TimeSpan.Zero"/> (default)
    /// refreshes them on every run.
    /// </summary>
    public TimeSpan ApexRefreshInterval { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// How many ladder entries to buffer before joining them against <c>riot_accounts</c> and
    /// writing the snapshots. A page holds ~205 entries, so the default is ~10 pages per join
    /// instead of one query per page.
    /// </summary>
    public int SaveBatchSize { get; set; } = 2000;
}
