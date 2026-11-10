using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Matchups;

/// <summary>
/// One opponent's line before projection: its games and wins, the field it is a share of,
/// and the lane counters behind it when any were measured.
/// </summary>
internal readonly record struct MatchupRow(int Opponent, int Games, int Wins, long TotalGames, LaneOutcome? LaneOutcome);

/// <summary>
/// Lane wins and losses past the threshold (evens are in neither), plus the summed
/// gold and experience gaps at 15 minutes, each over its own sample — smaller than
/// <see cref="Decided"/> on rows folded before #976 / #1111 respectively, which is
/// the reason all three carry separate denominators.
/// </summary>
internal readonly record struct LaneOutcome(
    int Wins, int Losses, long GoldDiffSum, int GoldDiffGames, long XpDiffSum, int XpDiffGames)
{
    public int Decided => Wins + Losses;
}

/// <summary>
/// The final projection both matchup reads share — the champion's (from the aggregate) and
/// one player's (live): rows mapped to entries ordered best-winrate first. The best / worst
/// <em>slicing</em> is the caller's, and reads the Wilson bounds rather than this order.
/// </summary>
internal static class MatchupEntries
{
    /// <summary>
    /// Every row is already above its games floor, so games is never zero.
    /// <paramref name="minDecidedLanes"/> is the decided-lane count under which the lane win
    /// rate is withheld.
    /// </summary>
    /// <remarks>
    /// The opponent id breaks ties. Win rates collide constantly at these sample sizes (every
    /// 1-of-2 matchup is 50%), and without a total order the same request can hand back the
    /// same rows in a different sequence — which reads as a data change, exactly as
    /// <c>ChampionDominantLaneFilter</c> argues.
    /// </remarks>
    public static List<ChampionMatchupEntry> ToOrdered(IEnumerable<MatchupRow> rows, int minDecidedLanes)
        => rows
            .Select(x =>
            {
                var (lower, upper) = RateMath.WilsonInterval(x.Wins, x.Games);
                return new ChampionMatchupEntry
                {
                    OpponentChampionId = x.Opponent,
                    Games = x.Games,
                    Wins = x.Wins,
                    WinRate = RateMath.Rate(x.Wins, x.Games),
                    PlayRate = RateMath.Rate(x.Games, x.TotalGames),
                    WinRateLowerBound = lower,
                    WinRateUpperBound = upper,
                    // Decided lanes only, and only enough of them. Too few yields null
                    // rather than a rate: "no lane was ever settled here", "one lane was
                    // settled and we won it" and "the lane is always lost" are three
                    // different facts and must not render alike.
                    DecidedLaneGames = x.LaneOutcome?.Decided ?? 0,
                    LaneWinRate = x.LaneOutcome is { } outcome && outcome.Decided >= Math.Max(1, minDecidedLanes)
                        ? RateMath.Rate(outcome.Wins, outcome.Decided)
                        : null,
                    // Averaged over the lanes the gap was measured on — never over decided
                    // lanes or games, both of which are larger and would drag the average
                    // toward zero by the share of lanes nobody ever measured (#976).
                    GoldDiffLaneGames = x.LaneOutcome?.GoldDiffGames ?? 0,
                    AverageGoldDiffAt15 = x.LaneOutcome is { GoldDiffGames: > 0 } gap
                        ? (double)gap.GoldDiffSum / gap.GoldDiffGames
                        : null,
                    XpDiffLaneGames = x.LaneOutcome?.XpDiffGames ?? 0,
                    AverageXpDiffAt15 = x.LaneOutcome is { XpDiffGames: > 0 } xp
                        ? (double)xp.XpDiffSum / xp.XpDiffGames
                        : null,
                };
            })
            .OrderByDescending(m => m.WinRate)
            .ThenBy(m => m.OpponentChampionId)
            .ToList();
}
