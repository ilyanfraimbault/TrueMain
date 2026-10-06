namespace Data.Entities;

/// <summary>
/// How a tracked player on a champion fared against one enemy champion on any lane
/// (#1713): the opposing-pair twin of <see cref="ChampionSynergyStat"/>. One row per
/// (champion, position, opponent, opponent position, patch, elo band), additive,
/// with no sample floor — the read folds rows to its scope and floors the total.
///
/// <para>
/// <c>champion_matchup_stats</c> already answers the lane opponent; this table is
/// what lets the draft weigh the enemy jungler or bot lane too. The lane opponent's
/// row is stored as well (the fold does not special-case it) and the draft read
/// leaves it to the matchup table.
/// </para>
/// </summary>
public class ChampionOpponentStat
{
    public Guid Id { get; set; }

    /// <summary>The tracked side: a main of this champion, in a game that is not a remake.</summary>
    public int ChampionId { get; set; }

    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>The enemy champion — whoever played it, tracked or not.</summary>
    public int OpponentChampionId { get; set; }

    public string OpponentPosition { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Per-tier elo band of the tracked player.</summary>
    public string EloBracket { get; set; } = string.Empty;

    public int Games { get; set; }

    /// <summary>Of <see cref="Games"/>, those the tracked player won.</summary>
    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
