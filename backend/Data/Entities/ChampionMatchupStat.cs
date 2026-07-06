namespace Data.Entities;

/// <summary>
/// Pre-aggregated champion-vs-lane-opponent record for the global matchups
/// leaderboard. One row per (champion, position, opponent, patch) slice over the
/// tracked-account population on the configured queue. Stores the additive facts
/// only — games and wins, with NO sample floor applied — so the read side can
/// fold rows to the requested patch scope (a single patch, or all patches summed)
/// and apply the games floor on the merged total. Replaces the per-request
/// self-join over <see cref="MatchParticipant"/> for the global slice (#606); the
/// player-scoped and opponent-search slices stay live.
/// </summary>
public class ChampionMatchupStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Lane of the champion side (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    public int OpponentChampionId { get; set; }

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// Per-tier elo band of the champion side (see <c>Core.Lol.Ranking.EloBracket</c>):
    /// the aggregate is split by the tracked player's rank at game time, so a
    /// rank-filtered read seeks the bands it wants and the unfiltered read sums
    /// every band. <c>UNRANKED</c> for games with no usable rank snapshot.
    /// </summary>
    public string EloBracket { get; set; } = string.Empty;

    public int Games { get; set; }

    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
