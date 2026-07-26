namespace Ingestor.Options;

public class MatchIngestionOptions
{
    public const string SectionName = "MatchIngestion";

    public int BatchSize { get; set; } = 50;

    public int MatchesPerAccount { get; set; } = 20;

    public int SaveBatchSizeMatches { get; set; } = 10;

    public int MaxMatchFetchConcurrency { get; set; } = 4;

    public int ClaimLeaseMinutes { get; set; } = 30;

    /// <summary>
    /// Optional narrowing override of the shared <c>Platforms:Active</c> list (#496). Left empty
    /// — the default — match ingestion inherits <see cref="PlatformScopeOptions.Active"/>, so a
    /// region is added in one place instead of three. An override must be a subset of the shared
    /// list; that is enforced at startup by <see cref="PlatformScopeValidator"/>.
    /// </summary>
    public List<string> Platforms { get; set; } = [];
}
