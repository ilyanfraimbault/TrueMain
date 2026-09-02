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

    /// <summary>
    /// Ranked games this account has played according to the most recent ladder reading —
    /// the sum of the wins and losses on its latest rank snapshot (#1360). Null when no
    /// reading has ever carried them (an unranked account, or a tier the ladder sweep does
    /// not cover).
    /// </summary>
    /// <remarks>
    /// Denormalised onto the account, rather than joined from <c>rank_snapshots</c> at claim
    /// time, because it exists to be part of an ORDER BY over the claimable set: a lateral
    /// join to the newest snapshot per account is exactly the query the claim cannot afford
    /// to run for every candidate row it considers.
    /// </remarks>
    public int? LadderGames { get; set; }

    /// <summary>
    /// The value <see cref="LadderGames"/> held when this account's matches were last
    /// ingested (#1360). The difference between the two is how many ranked games the player
    /// has played since we last looked — the signal the claim orders by.
    /// </summary>
    public int? LadderGamesAtLastIngest { get; set; }

    /// <summary>
    /// Last time <c>MainActivityProcess</c> asked champion mastery whether this
    /// account still plays its mains (#900). Throttles that one call per account
    /// per <c>MainActivity:RecheckAfterHours</c> and orders the selection
    /// (never-checked first, then oldest).
    /// </summary>
    public DateTime? LastActivityCheckAtUtc { get; set; }

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
