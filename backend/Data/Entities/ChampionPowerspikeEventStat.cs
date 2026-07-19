namespace Data.Entities;

/// <summary>
/// Pre-aggregated power-spike of one event (a level milestone or an item
/// completion) for a (champion, position, patch, elo, event) slice (#694).
///
/// A spike is the slope-change of the opponent-relative power around the event
/// minute — <c>(P(e+3) − 2·P(e) + P(e−3)) / 3</c> — which is linear in the per-game
/// gold/damage diffs, so it is computed per game at aggregation time (while the
/// dense per-minute snapshots still exist) and only the additive
/// <see cref="SumSpike"/> / <see cref="SumMinute"/> / <see cref="Games"/> are kept.
/// The read folds to the requested scope, divides by games, and — for item events —
/// intersects <see cref="RefId"/> with the champion's dominant build for display.
/// </summary>
public class ChampionPowerspikeEventStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Lane of the champion side (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Per-tier elo band of the champion side (see <c>Core.Lol.Ranking.EloBracket</c>).</summary>
    public string EloBracket { get; set; } = string.Empty;

    /// <summary>Event kind: "level" (milestone) or "item" (completion).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The level number (6/11/16) for "level", or the item id for "item".</summary>
    public int RefId { get; set; }

    /// <summary>Sum over games of the per-game slope-change spike magnitude.</summary>
    public double SumSpike { get; set; }

    /// <summary>Sum over games of the per-game event minute (for the average timing).</summary>
    public double SumMinute { get; set; }

    public int Games { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
