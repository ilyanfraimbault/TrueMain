namespace Data.Entities;

/// <summary>
/// Master row of the aggregate schema: one row per
/// (riot_account_id, champion_id, game_version, platform_id, queue_id, position, elo_bracket)
/// slice, carrying the scope-level totals (Games / Wins / aggregated-at).
/// Per-combo counts live on <see cref="ChampionAggregatePattern"/> with
/// FKs to the deduplicated <c>ChampionDim*</c> tables — the scope itself
/// owns no dimension rows, only the slice identity and its totals.
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

    /// <summary>
    /// Whether the contributing account was a <em>main</em> of this champion
    /// (<c>main_champion_stats.IsMain</c>) when the slice was aggregated — the
    /// site's "truemain" population.
    ///
    /// <para>
    /// It is a property of the whole row, never of individual games: a scope is
    /// keyed on one account, one champion and one platform, which is exactly the
    /// key of <c>main_champion_stats</c>, so main-ness cannot vary inside it.
    /// That is what lets the truemains filter be a single boolean here instead of
    /// a duplicated population — reads narrow with <c>WHERE "IsMain"</c> and the
    /// unfiltered read is the superset.
    /// </para>
    ///
    /// <para>
    /// Frozen with the rest of the slice: it records what the account was when
    /// the aggregate was built, not what it is now. Main analysis re-runs and can
    /// demote an account, but an old patch's scopes are never re-aggregated (see
    /// the live-patch cleanup rule in the source-row reader), so their flag stays
    /// as it was — consistent with every other number on the row.
    /// </para>
    ///
    /// <para>
    /// Scopes aggregated before this column existed default to <see langword="true"/>:
    /// every row the pipeline produced back then came through an <c>IsMain</c>
    /// filter, so "true" is the historically accurate value, not a guess.
    /// </para>
    /// </summary>
    public bool IsMain { get; set; }

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
