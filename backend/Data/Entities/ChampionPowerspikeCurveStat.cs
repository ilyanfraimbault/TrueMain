namespace Data.Entities;

/// <summary>
/// Pre-aggregated point of the champion power curve: additive TOTALS of the
/// per-game gold/damage lead over the lane opponent, plus the game count, for one
/// (champion, position, patch, elo, interval-minute) slice over the tracked-account
/// population on the configured queue (#694).
///
/// Unlike <see cref="ChampionTimelineLeadStat"/> this carries EVERY minute mark
/// (1..30), not just the five canonical ones — the power curve is drawn per minute.
/// The curve mean at minute m is linear once the global per-minute spread is fixed
/// (<see cref="PowerspikeSigmaStat"/>): mean power =
/// 0.5·(TotalGoldDiff/Games)/σ_gold(m) + 0.5·(TotalDamageDiff/Games)/σ_dmg(m). So
/// the read folds these rows to the requested patch scope, divides totals by games,
/// and applies the games floor on the merged total — no access to the raw
/// <see cref="MatchParticipantTimelineSnapshot"/> rows, which are then prunable
/// down to the canonical marks.
/// </summary>
public class ChampionPowerspikeCurveStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Lane of the champion side (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// Per-tier elo band of the champion side (see <c>Core.Lol.Ranking.EloBracket</c>):
    /// the aggregate is split by the tracked player's rank at game time, so a
    /// rank-filtered read seeks the bands it wants and the unfiltered read sums
    /// every band. <c>UNRANKED</c> for games with no usable rank snapshot.
    /// </summary>
    public string EloBracket { get; set; } = string.Empty;

    /// <summary>Minute mark of the curve point (1..30).</summary>
    public int IntervalMinute { get; set; }

    public int Games { get; set; }

    /// <summary>Sum over games of (champion TotalGold − opponent TotalGold) at the mark.</summary>
    public long TotalGoldDiff { get; set; }

    /// <summary>Sum over games of (champion DamageToChampions − opponent DamageToChampions) at the mark.</summary>
    public long TotalDamageDiff { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
