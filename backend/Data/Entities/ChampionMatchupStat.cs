namespace Data.Entities;

/// <summary>
/// Pre-aggregated champion-vs-lane-opponent record for the global matchups
/// leaderboard. One row per (champion, position, opponent, patch) slice on the
/// configured queue, counting the games of players who are <b>mains of the
/// champion side</b> — see <c>Data.Aggregation.MatchupCohort</c>, which both folds
/// share — against whoever held that lane. Stores the additive facts only, with NO
/// sample floor applied, so the read side can fold rows to the requested patch
/// scope (a single patch, or all patches summed) and apply its floors on the
/// merged total. Replaces the per-request self-join over
/// <see cref="MatchParticipant"/> for every global slice (#606); only the
/// player-scoped slice stays live, having no dimension here.
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

    /// <summary>
    /// Sum of the champion's gold gap over its lane opponent at 15 minutes, over the
    /// lanes counted in <see cref="LaneGoldDiffGames"/> (#976). Signed, and summed
    /// rather than bucketed so the read side owns the band edges: moving the
    /// "even / good / dominant" cutoffs is then a product decision with no re-fold,
    /// unlike <c>LaneOutcomeAggregation:GoldLeadThreshold</c>, which re-defines the
    /// stored win/loss counters.
    /// </summary>
    public long LaneGoldDiffSum { get; set; }

    /// <summary>
    /// Lanes <see cref="LaneGoldDiffSum"/> covers — the average gap is
    /// <c>LaneGoldDiffSum / LaneGoldDiffGames</c>, and is unknown when this is 0.
    ///
    /// <para>
    /// Deliberately a second denominator rather than a reuse of <see cref="LaneGames"/>,
    /// which counts exactly the same lanes going forward. Rows folded before #976 have
    /// <see cref="LaneGames"/> &gt; 0 and no sum: dividing by <see cref="LaneGames"/>
    /// would report those as a +0 gold gap — the most confident-looking verdict there
    /// is — out of data that was never collected. The fold is additive and frozen
    /// patches can never be recomputed (#466), so those rows stay honestly incomplete
    /// and the two counters converge only as the backlog drains.
    /// </para>
    /// </summary>
    public int LaneGoldDiffGames { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
