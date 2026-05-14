namespace Data.Entities;

/// <summary>
/// Master row of the aggregate schema: one row per
/// (riot_account_id, champion_id, game_version, platform_id, queue_id, position)
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

    public int Games { get; set; }
    public int Wins { get; set; }
    public DateTime LastGameStartTimeUtc { get; set; }
    public DateTime AggregatedAtUtc { get; set; }
}
