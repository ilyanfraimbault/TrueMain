namespace Data.Entities;

/// <summary>
/// Master row of the aggregate schema: one row per
/// (riot_account_id, champion_id, game_version, platform_id, queue_id, position, elo_bracket)
/// slice, carrying the scope-level totals (Games / Wins / aggregated-at).
/// Per-combo counts live on <see cref="ChampionAggregatePattern"/> with
/// FKs to the deduplicated <c>ChampionDim*</c> tables — the scope itself
/// no longer owns dimension rows directly (Phase 6 removed the
/// per-scope dim tables in favour of the junction).
/// </summary>
public class ChampionAggregateScope
{
    public Guid Id { get; set; }

    public Guid RiotAccountId { get; set; }
    public RiotAccount RiotAccount { get; set; } = null!;
    public int ChampionId { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public int QueueId { get; set; }
    public string Position { get; set; } = string.Empty;

    /// <summary>
    /// Elo bucket of the contributing games, derived from the player's ranked
    /// tier at game time — the nearest <c>rank_snapshots</c> capture to each
    /// game's start (see <c>Core.Lol.Ranking.EloBracket</c>). One scope row per
    /// persisted bracket; the synthetic <c>ALL</c> bracket is the read-time
    /// union of these rows and is never stored.
    /// </summary>
    public string EloBracket { get; set; } = string.Empty;

    public int Games { get; set; }
    public int Wins { get; set; }

    /// <summary>
    /// Kill / death / assist totals summed across the scope's contributing
    /// games. Lets the truemains leaderboard derive a player's KDA from the
    /// frozen aggregates instead of live <c>match_participants</c> (which
    /// retention hard-deletes beyond the last few patches). Scopes aggregated
    /// before these columns existed carry 0 until re-aggregated — frozen
    /// old-patch scopes never are, so their KDA stays understated by design.
    /// </summary>
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    public DateTime LastGameStartTimeUtc { get; set; }
    public DateTime AggregatedAtUtc { get; set; }
}
