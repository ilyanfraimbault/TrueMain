namespace Data.Entities;

/// <summary>
/// Pre-aggregated power-spike of one event (a level milestone or an item
/// completion) for a (champion, position, patch, elo, core build, lane opponent,
/// event) slice (#694, scoped per core build in #890, per opponent in #957).
///
/// A spike is the slope-change of the opponent-relative power around the event
/// minute — <c>(P(e+3) − 2·P(e) + P(e−3)) / 3</c> — which is linear in the per-game
/// gold/damage diffs, so it is computed per game at aggregation time (while the
/// dense per-minute snapshots still exist) and only the additive
/// <see cref="SumSpike"/> / <see cref="SumMinute"/> / <see cref="Games"/> are kept.
/// The read folds to the requested scope and divides by games.
///
/// Rows are scoped to the core build the game belonged to
/// (<see cref="BuildFirstItemId"/> + <see cref="BuildKeystoneId"/>), so a champion
/// built two different ways yields two independent sets of item spikes instead of
/// one blend. That dimension has a long tail of rare combinations, so retention
/// prunes rows below the read's games floor rather than letting it accumulate.
///
/// Rows are also scoped to the lane opponent the spike was measured against
/// (<see cref="OpponentChampionId"/>, #957). This is not a new fact: the spike is
/// <em>defined</em> as the power relative to that opponent, so the fold already had
/// the id in hand and used to discard it. Splitting the grain on it is exact — every
/// game belongs to exactly one opponent, so the unscoped read recovers today's
/// numbers by summing across opponents, which is what it already did by grouping on
/// (<see cref="EventType"/>, <see cref="RefId"/>).
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

    /// <summary>
    /// First item of the core build this game belonged to — the same
    /// <c>BuildItem0</c> the builds read groups its tabs by, so the two join.
    /// </summary>
    public int BuildFirstItemId { get; set; }

    /// <summary>
    /// Primary keystone of the core build this game belonged to. Pairs with
    /// <see cref="BuildFirstItemId"/> to form the builds read's <c>BuildKey</c>.
    /// </summary>
    public int BuildKeystoneId { get; set; }

    /// <summary>
    /// Champion the spike was measured against — the same-lane participant on the
    /// other team, whose gold/damage the power index subtracts (#957).
    ///
    /// <para>
    /// <c>0</c> means "not recorded": rows folded before #957 blend every opponent
    /// together, and rows collapsed by retention once their patch left the live
    /// window are deliberately rolled back to it. Both still count in the unscoped
    /// read; neither can answer a matchup filter, which is why matchup coverage
    /// starts empty and fills in as new matches fold.
    /// </para>
    /// </summary>
    public int OpponentChampionId { get; set; }

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
