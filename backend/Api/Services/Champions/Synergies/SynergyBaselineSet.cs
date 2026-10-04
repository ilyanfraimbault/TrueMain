using Core.Lol.Synergy;
using Data.Entities;

namespace TrueMain.Services.Champions.Synergies;

/// <summary>
/// One folded row of <c>champion_synergy_baseline_stats</c>: a champion's games and
/// wins at a lane on one side (<see cref="SynergyBaselineSide.Self"/> or
/// <see cref="SynergyBaselineSide.Ally"/>), summed over the requested scope.
/// </summary>
internal sealed record SynergyBaselineRow(string Side, int ChampionId, string TeamPosition, int Games, int Wins);

/// <summary>A marginal sample — games and wins — read out of a <see cref="SynergyBaselineSet"/>.</summary>
internal readonly record struct SynergyMarginal(int Games, int Wins)
{
    public static SynergyMarginal Empty { get; } = new(0, 0);

    public double WinRate => RateMath.Rate(Wins, Games);
}

/// <summary>
/// The marginals for one scope, indexed for lookup — the inputs of the expected
/// win rate <see cref="SynergyMath.ExpectedWinRate"/> builds, kept apart from the
/// query service so the model can be exercised without a database.
/// <see cref="CohortGames"/> / <see cref="CohortWins"/> sum the <c>SELF</c> side,
/// which is exactly one row per tracked participant per folded match — so the
/// cohort rate is the tracked population's overall win rate and never
/// double-counts a game the way summing the four-per-participant <c>ALLY</c> side
/// would.
/// </summary>
internal sealed class SynergyBaselineSet
{
    private readonly Dictionary<(int ChampionId, string Position), SynergyMarginal> _self;
    private readonly Dictionary<(int ChampionId, string Position), SynergyMarginal> _ally;

    /// <summary>Ally games per champion summed over every lane — <see cref="IsRealLane"/>'s denominator.</summary>
    private readonly Dictionary<int, int> _laneTotals;

    private SynergyBaselineSet(
        Dictionary<(int, string), SynergyMarginal> self,
        Dictionary<(int, string), SynergyMarginal> ally,
        Dictionary<int, int> laneTotals,
        int cohortGames,
        int cohortWins)
    {
        _self = self;
        _ally = ally;
        _laneTotals = laneTotals;
        CohortGames = cohortGames;
        CohortWins = cohortWins;
    }

    public int CohortGames { get; }

    public int CohortWins { get; }

    public double CohortWinRate => RateMath.Rate(CohortWins, CohortGames);

    public static SynergyBaselineSet From(IReadOnlyList<SynergyBaselineRow> rows)
    {
        var self = new Dictionary<(int, string), SynergyMarginal>();
        var ally = new Dictionary<(int, string), SynergyMarginal>();
        var laneTotals = new Dictionary<int, int>();
        var cohortGames = 0;
        var cohortWins = 0;

        foreach (var row in rows)
        {
            if (string.Equals(row.Side, SynergyBaselineSide.Self, StringComparison.Ordinal))
            {
                self[(row.ChampionId, row.TeamPosition)] = new SynergyMarginal(row.Games, row.Wins);
                cohortGames += row.Games;
                cohortWins += row.Wins;
            }
            else if (string.Equals(row.Side, SynergyBaselineSide.Ally, StringComparison.Ordinal))
            {
                ally[(row.ChampionId, row.TeamPosition)] = new SynergyMarginal(row.Games, row.Wins);
                laneTotals[row.ChampionId] = laneTotals.GetValueOrDefault(row.ChampionId, 0) + row.Games;
            }
        }

        return new SynergyBaselineSet(self, ally, laneTotals, cohortGames, cohortWins);
    }

    /// <summary>The champion's own marginal, or an empty one when it has no games in scope.</summary>
    public SynergyMarginal Self(int championId, string position)
        => _self.GetValueOrDefault((championId, position), SynergyMarginal.Empty);

    /// <summary>The champion's marginal as somebody's teammate, or an empty one.</summary>
    public SynergyMarginal Ally(int championId, string position)
        => _ally.GetValueOrDefault((championId, position), SynergyMarginal.Empty);

    /// <summary>
    /// Expected win rate of the tracked side (<paramref name="selfWinRate"/>) grouped
    /// with teammates of the given marginal rates, anchored on this scope's cohort
    /// rate — the reference every synergy is measured against.
    /// </summary>
    public double ExpectedWinRate(double selfWinRate, ReadOnlySpan<double> allyWinRates)
        => SynergyMath.ExpectedWinRate(selfWinRate, allyWinRates, CohortWinRate);

    /// <summary>
    /// Whether <paramref name="position"/> is a lane this champion actually plays:
    /// its share of the champion's ally games across every lane, against
    /// <paramref name="minLanePlayRate"/>. A champion with no ally games at all in
    /// scope fails — nothing is known about its roles, and the caller's other
    /// floors have already established the pairing is thin.
    ///
    /// <para>
    /// The denominator is the whole <c>ALLY</c> side for that champion, which is
    /// why this lives on the baseline set rather than being derived from the
    /// pairing rows: those are already filtered to one champion's teammates and to
    /// lanes other than its own, so a share computed from them would measure the
    /// wrong thing — Udyr would read as a 100% toplaner on a jungler's page purely
    /// because his jungle games cannot appear there.
    /// </para>
    /// </summary>
    public bool IsRealLane(int championId, string position, double minLanePlayRate)
    {
        if (minLanePlayRate <= 0d)
        {
            return true;
        }

        var lane = Ally(championId, position).Games;
        if (lane == 0)
        {
            return false;
        }

        var acrossLanes = _laneTotals.GetValueOrDefault(championId, 0);
        return acrossLanes > 0 && (double)lane / acrossLanes >= minLanePlayRate;
    }
}
