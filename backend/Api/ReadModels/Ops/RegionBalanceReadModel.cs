namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Per-platform balance on the health cockpit (#1153): is ingestion spread across the regions
/// the way the claim allocator (#1150) means it to be?
///
/// <para>
/// The 82/14/4 imbalance behind #1150 took a manual investigation to find, because the only
/// per-region view was one run summary's <c>byPlatform</c> split. This answers the question
/// from one payload: what each region holds, what it ingested over a window and how that
/// moved day by day, and the coverage deficit the allocator weighs each region's share of the
/// claim by — computed with the same arithmetic (<c>Core.Coverage.CoverageDeficit</c>) over
/// the same counts (<c>Data.Queries.ActiveMainCoverageQuery</c>).
/// </para>
/// </summary>
public sealed record RegionBalanceReadModel
{
    /// <summary>Days covered by <see cref="DailyMatches"/> and each platform's window total.</summary>
    public int WindowDays { get; init; }

    /// <summary>Start of the first day in the window (UTC midnight).</summary>
    public DateTime WindowStartUtc { get; init; }

    /// <summary>
    /// <c>Coverage:TargetMainsPerChampion</c> as the Ingestor published it at its last boot.
    /// Null when it has not published one; every deficit is then null too, with
    /// <see cref="CoverageUnknownReason"/> saying why — a deficit computed against a guessed
    /// target would be one the allocator never used.
    /// </summary>
    public int? TargetMainsPerChampion { get; init; }

    /// <summary>
    /// The platforms the match-ingest claim splits its batch across
    /// (<c>MatchIngestion:Platforms</c>, which inherits <c>Platforms:Active</c>), as the Ingestor
    /// published them. Null when unknown.
    /// </summary>
    public IReadOnlyList<string>? ClaimPlatforms { get; init; }

    /// <summary>When the Ingestor published the two values above. Null when it never has.</summary>
    public DateTime? ConfigurationCapturedAtUtc { get; init; }

    /// <summary>Why the coverage columns are null. Set iff <see cref="TargetMainsPerChampion"/> is null.</summary>
    public string? CoverageUnknownReason { get; init; }

    /// <summary>
    /// Champions with an active main on any platform — the denominator of every deficit and
    /// of every below-target share. 0 on a cold start, where the allocator splits evenly.
    /// </summary>
    public int ChampionUniverse { get; init; }

    /// <summary>One row per platform the claim covers or the corpus holds, claim platforms first.</summary>
    public IReadOnlyList<PlatformBalanceReadModel> Platforms { get; init; } = [];

    /// <summary>
    /// Matches ingested per platform per UTC day over the window, by ingestion time (not game
    /// time). A day a platform ingested nothing has no row; the client fills the zero.
    /// </summary>
    public IReadOnlyList<PlatformDailyMatchesReadModel> DailyMatches { get; init; } = [];

    /// <summary>
    /// Set when the panel could not be measured at all; everything else is then empty. The
    /// cockpit renders the reason in place rather than an empty table that reads as "no regions".
    /// </summary>
    public string? UnknownReason { get; init; }
}

public sealed record PlatformBalanceReadModel
{
    public string PlatformId { get; init; } = string.Empty;

    /// <summary>
    /// Whether the claim allocates to this platform. False for a platform the corpus still holds
    /// but the claim no longer covers (e.g. after narrowing <c>Platforms:Active</c>); null when
    /// the claim's platform list is unknown.
    /// </summary>
    public bool? InClaim { get; init; }

    /// <summary>Tracked Riot accounts on the platform, any status.</summary>
    public int Accounts { get; init; }

    /// <summary>Distinct accounts holding at least one active main on the platform.</summary>
    public int ActiveMainAccounts { get; init; }

    /// <summary>Matches of the configured queue ingested on the platform within the window.</summary>
    public long MatchesInWindow { get; init; }

    /// <summary>This platform's share of every platform's <see cref="MatchesInWindow"/>; null when the total is 0.</summary>
    public double? MatchShare { get; init; }

    /// <summary>Mean per-champion coverage deficit in [0, 1] — the allocator's signal. Null when the target is unknown.</summary>
    public double? MeanCoverageDeficit { get; init; }

    /// <summary>Champions of the universe below the target on this platform. Null when unknown or the universe is empty.</summary>
    public int? ChampionsBelowTarget { get; init; }

    /// <summary><see cref="ChampionsBelowTarget"/> over <see cref="RegionBalanceReadModel.ChampionUniverse"/>.</summary>
    public double? ChampionsBelowTargetShare { get; init; }

    /// <summary>
    /// The share of a claim batch the allocator gives this platform from the deficits above:
    /// <c>(1 + deficit) / Σ(1 + deficit)</c> over the claim platforms, before the largest-remainder
    /// rounding of an actual batch. Null for a platform outside the claim, or when unknown.
    /// </summary>
    public double? ClaimShare { get; init; }
}

public sealed record PlatformDailyMatchesReadModel
{
    /// <summary>The UTC day, <c>YYYY-MM-DD</c>.</summary>
    public string Day { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public long Matches { get; init; }
}
