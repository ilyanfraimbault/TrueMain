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
    /// Share of each claim batch reserved for accounts that are already active established
    /// mains, the remainder going to freshly <c>Queued</c> candidates (#900). Depth over
    /// breadth: past <c>Coverage:TargetMainsPerChampion</c>, more games from the mains we
    /// track is worth more than more distinct mains per champion, and every match-v5 call
    /// spent on a new candidate is one not spent deepening an existing main's history.
    ///
    /// A floor, not a partition: whatever one class cannot fill spills to the other, so a
    /// run always uses its full batch (same semantics as <c>Harvest:NewCandidateShare</c>,
    /// #495). <c>0</c> restores the pre-#900 behaviour of a single oldest-first queue, and
    /// <c>1</c> still lets new candidates use whatever budget established mains leave.
    /// </summary>
    public double EstablishedMainShare { get; set; } = 0.7;

    /// <summary>
    /// Optional narrowing override of the shared <c>Platforms:Active</c> list (#496). Left empty
    /// — the default — match ingestion inherits <see cref="PlatformScopeOptions.Active"/>, so a
    /// region is added in one place instead of three. An override must be a subset of the shared
    /// list; that is enforced at startup by <see cref="PlatformScopeValidator"/>.
    /// </summary>
    public List<string> Platforms { get; set; } = [];
}
