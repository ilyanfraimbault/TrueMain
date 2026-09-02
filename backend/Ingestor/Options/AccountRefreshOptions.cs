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

    /// <summary>
    /// How recently an account's profile must have been synced for this process to skip its
    /// account-v1 by-puuid call (#1358).
    /// <para>
    /// The profile mirror of <see cref="RankSyncFreshness"/>. Reaching the head of the refresh
    /// queue is not on its own a reason to spend a call: game names and tag lines change
    /// rarely, so a profile read a day ago rewrites the same two strings. A week is long enough
    /// that a rename still surfaces within one sweep of the queue, and short enough that the
    /// call is not what decides whether the row moves.
    /// </para>
    /// <para>
    /// The skip never applies to an account whose identity is still incomplete: those are what
    /// the selection's identity buckets exist to drain, and account-v1 is the only thing that
    /// can fill them. <see cref="TimeSpan.Zero"/> disables the skip.
    /// </para>
    /// </summary>
    public TimeSpan ProfileSyncFreshness { get; set; } = TimeSpan.FromDays(7);
}
