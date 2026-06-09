namespace Data.Entities;

/// <summary>
/// A request to bring a single account into the pipeline by its Riot ID
/// (<see cref="GameName"/> + <see cref="TagLine"/> on <see cref="PlatformId"/>),
/// instead of waiting for the ladder <c>DiscoveryProcess</c> to surface it. The
/// API records a row at <see cref="SeedRequestStatus.Pending"/>; the Ingestor's
/// <c>ManualSeedProcess</c> claims it, resolves the PUUID via account-v1, upserts
/// the <see cref="RiotAccount"/> and its mastery-derived candidates, and stamps
/// the terminal status. This is the shared backbone for the admin "add a main"
/// panel (#410) and bulk OTP import (#411).
/// </summary>
public class SeedRequest
{
    public Guid Id { get; set; }

    /// <summary>The Riot ID game name, as submitted (trimmed).</summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>The Riot ID tag line, as submitted (trimmed, without the leading '#').</summary>
    public string TagLine { get; set; } = string.Empty;

    /// <summary>The platform the account belongs to (e.g. "EUW1"); a <c>PlatformRoute</c> name.</summary>
    public string PlatformId { get; set; } = string.Empty;

    public SeedRequestStatus Status { get; set; } = SeedRequestStatus.Pending;

    /// <summary>Failure detail when <see cref="Status"/> is <see cref="SeedRequestStatus.Failed"/>; else null.</summary>
    public string? Error { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    /// <summary>When the Ingestor reached a terminal state (Ingested/Failed); null while unprocessed.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>The PUUID account-v1 resolved for this Riot ID; null until ingested.</summary>
    public string? ResolvedPuuid { get; set; }

    /// <summary>The upserted <see cref="RiotAccount"/>'s id; null until ingested.</summary>
    public Guid? ResolvedRiotAccountId { get; set; }
}

/// <summary>
/// Lifecycle of a <see cref="SeedRequest"/>. <see cref="Pending"/> and
/// <see cref="Resolving"/> are the unprocessed states the idempotency check and
/// the Ingestor's claim scan look at; <see cref="Ingested"/> and
/// <see cref="Failed"/> are terminal. Stored as the enum name (text) so the
/// column is human-readable in ad-hoc SQL.
/// </summary>
public enum SeedRequestStatus
{
    Pending = 0,
    Resolving = 1,
    Ingested = 2,
    Failed = 3
}
