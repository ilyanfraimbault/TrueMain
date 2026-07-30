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

    /// <summary>
    /// Matches in this matchup where BOTH lane participants have a 15-minute timeline
    /// snapshot, i.e. where a lane outcome could be judged at all (#919). Deliberately
    /// separate from <see cref="Games"/>: a match with no ingested timeline, or one that
    /// ended before the 15-minute mark, counts as a game but not as a judgeable lane.
    /// Dividing lane wins by <see cref="Games"/> would silently understate every lane
    /// win rate by the share of games without a snapshot.
    /// </summary>
    public int LaneGames { get; set; }

    /// <summary>
    /// Of <see cref="LaneGames"/>, those where the champion was ahead of its lane
    /// opponent by more than the configured gold threshold at 15 minutes.
    /// </summary>
    public int LaneWins { get; set; }

    /// <summary>
    /// Of <see cref="LaneGames"/>, those where the champion was *behind* by more than
    /// the same threshold. Stored rather than derived because a threshold creates a
    /// third outcome: lanes inside the band are neither won nor lost, and
    /// <c>LaneGames - LaneWins</c> would count those as losses. The even count is
    /// <c>LaneGames - LaneWins - LaneLosses</c>, and the lane win rate divides by
    /// <c>LaneWins + LaneLosses</c> — the decided lanes only.
    /// </summary>
    public int LaneLosses { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
