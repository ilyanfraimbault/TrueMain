namespace Ingestor.Options;

public class AccountRefreshOptions
{
    public const string SectionName = "AccountRefresh";

    public int BatchSize { get; set; } = 200;

    public TimeSpan RankSyncFreshness { get; set; } = TimeSpan.FromMinutes(15);
}
