namespace Data.Entities;

/// <summary>
/// One champion-select ban of a match (#920), straight from match-v5's
/// <c>teams[].bans[]</c>. Ten rows per match at most, so the table is kept as
/// narrow as it can be: the natural key <c>(MatchId, TeamId, PickTurn)</c> is the
/// primary key rather than a surrogate <see cref="Guid"/>, which saves both the
/// 16-byte column and the second index a surrogate would need to enforce the same
/// uniqueness — disk being the production constraint it is (#680).
///
/// Unused ban slots (Riot sends <c>championId = -1</c> when a player let the timer
/// run out) are dropped at ingestion rather than stored as sentinel rows.
///
/// Dies with its match on retention through the cascading FK, so ban data only
/// ever exists for the patches whose matches are still retained. The aggregates in
/// <see cref="ChampionBanStat"/> are what survives.
/// </summary>
public class MatchBan
{
    public required string MatchId { get; init; }

    /// <summary>Riot team id (100 / 200) that spent the ban.</summary>
    public int TeamId { get; init; }

    /// <summary>Global ban order within the match (1-10 in a standard draft).</summary>
    public int PickTurn { get; init; }

    public int ChampionId { get; init; }
}
