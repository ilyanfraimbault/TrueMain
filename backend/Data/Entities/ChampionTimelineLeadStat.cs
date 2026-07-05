namespace Data.Entities;

/// <summary>
/// Pre-aggregated "lead vs lane opponent" record for the champion timeline curve.
/// One row per (champion, position, patch, interval-minute) slice over the
/// tracked-account population on the configured queue. Stores additive TOTALS of
/// the per-game diffs (gold/cs/kills/level/xp/damage) plus the game count, with NO
/// sample floor applied, so the read side can fold rows to the requested patch
/// scope, divide totals by games for the average, and apply the games floor on the
/// merged total. Replaces the per-request triple self-join over
/// <see cref="MatchParticipantTimelineSnapshot"/> (#606).
/// </summary>
public class ChampionTimelineLeadStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Lane of the champion side (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Canonical minute mark (5/10/15/20/30).</summary>
    public int IntervalMinute { get; set; }

    public int Games { get; set; }

    public long TotalGoldDiff { get; set; }

    public long TotalCsDiff { get; set; }

    public long TotalKillsDiff { get; set; }

    public long TotalLevelDiff { get; set; }

    public long TotalXpDiff { get; set; }

    public long TotalDamageDiff { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
