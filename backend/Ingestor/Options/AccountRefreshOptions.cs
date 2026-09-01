namespace Ingestor.Options;

public class AccountRefreshOptions
{
    public const string SectionName = "AccountRefresh";

    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// How many accounts are refreshed per save + change-tracker drain (#1229). The run
    /// holds a single EF session across two Riot calls per account, so without a bound the
    /// tracker accumulated the whole <see cref="BatchSize"/> worth of accounts and rank
    /// snapshots for the duration of hundreds of HTTP round-trips, and every SaveChanges
    /// re-ran DetectChanges over all of them.
    /// </summary>
    public int SaveBatchSize { get; set; } = 25;

    /// <summary>
    /// How recently an account's rank must have been captured for this process to skip its
    /// league-v4 by-puuid call.
    /// <para>
    /// Sized against <c>LadderSyncProcess</c> (#1312), which now refreshes every tracked
    /// Master+ account on each cycle and sweeps the tiers below Master continuously. At the
    /// former 15-minute value this gate expired long before the next sweep came round, so the
    /// per-account call was re-issued anyway and none of the budget the ladder read saves was
    /// actually reallocated. Half a day is short enough that an account the sweep stops seeing
    /// — a demotion out of the swept range, or a decayed account — still falls back to the
    /// per-account path within one cycle.
    /// </para>
    /// </summary>
    public TimeSpan RankSyncFreshness { get; set; } = TimeSpan.FromHours(12);
}
