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

    public TimeSpan RankSyncFreshness { get; set; } = TimeSpan.FromMinutes(15);
}
