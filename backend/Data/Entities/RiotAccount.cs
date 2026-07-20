namespace Data.Entities;

public class RiotAccount
{
    public Guid Id { get; set; }

    // required (not init): Puuid and PlatformId are mandatory at construction, but
    // both are reassigned by the pipeline — Puuid on 404 re-resolution
    // (AccountRefreshProcess) and PlatformId on a region-transfer upsert
    // (AccountUpsertService) — so they keep a settable accessor.
    public required string Puuid { get; set; }

    // Not required: account-v1 owns GameName/TagLine, so a summoner-v4 insert
    // (AccountUpsertService) deliberately leaves the empty-string default until the
    // next AccountRefresh cycle resolves the Riot ID.
    public string GameName { get; set; } = string.Empty;

    public string? TagLine { get; set; }

    public required string PlatformId { get; set; }

    public Guid? PersonaId { get; set; }

    public Persona? Persona { get; set; }

    public string? SummonerId { get; set; }

    public int ProfileIconId { get; set; }

    public int SummonerLevel { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastProfileSyncAtUtc { get; set; }

    public DateTime? LastRankSyncAtUtc { get; set; }

    public DateTime? LastMainCalcAtUtc { get; set; }

    public DateTime? LastMatchIngestAtUtc { get; set; }

    public MatchIngestStatus MatchIngestStatus { get; set; } = MatchIngestStatus.Idle;

    public DateTime? MatchIngestClaimedAtUtc { get; set; }

    /// <summary>
    /// Lifecycle state against the Riot API. <see cref="RiotAccountStatus.Invalid"/>
    /// rows no longer resolve by PUUID and are skipped by every refresh/ingest
    /// selection so the pipeline stops retrying a permanent 404.
    /// </summary>
    public RiotAccountStatus Status { get; set; } = RiotAccountStatus.Active;

    /// <summary>
    /// Denormalised leaderboard sort key derived from the account's latest
    /// rank (tier/division/LP), maintained by the rank ingestion writer. Null
    /// when the account has no known/ranked tier (sorts last). This is an
    /// ordering key, not a displayed rank.
    /// </summary>
    public int? Score { get; set; }
}
