using Core.Lol.Draft;
using Core.Lol.Ranking;
using Core.Lol.Synergy;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Draft;

public interface IDraftEnemyReader
{
    /// <summary>
    /// Each candidate's measured pairing against each enemy on the board, on the
    /// lanes other than ours (#1713): observed minus expected win rate, with its games.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<int, DraftComponent>>> ReadAsync(
        IReadOnlyCollection<int> candidates,
        string position,
        IReadOnlyCollection<int> enemies,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct);
}

/// <summary>
/// Reads <c>champion_opponent_stats</c> and its baselines into the enemy-team term.
/// </summary>
/// <remarks>
/// <para>
/// The metric is the synergy one turned against enemies: the tracked side's own
/// rate and the enemy's rate as faced by tracked players, combined in log-odds
/// against the cohort intercept (<see cref="SynergyMath"/>), subtracted from the
/// observed rate. So a strong enemy that every champion loses to is not held
/// against any one of them — only what this candidate does into it beyond that.
/// </para>
/// <para>
/// The enemy's own lane is ignored, past "not ours": the lane opponent belongs to
/// <c>champion_matchup_stats</c>, and the rest of the enemy team is weighed whatever
/// lane the guess gives it, so a wrong guess between jungle and support costs nothing.
/// Patch fallback per candidate, by the rule of <see cref="DraftLaneReader"/>.
/// </para>
/// </remarks>
public sealed class DraftEnemyReader(TrueMainDbContext db, IChampionReadCache cache) : IDraftEnemyReader
{
    /// <summary>The synergy service's baseline floor (<c>ChampionsList:MinSynergyBaselineGames</c>'s default).</summary>
    internal const int MinBaselineGames = 50;

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<int, DraftComponent>>> ReadAsync(
        IReadOnlyCollection<int> candidates,
        string position,
        IReadOnlyCollection<int> enemies,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
    {
        if (candidates.Count == 0 || enemies.Count == 0)
        {
            return new Dictionary<int, IReadOnlyDictionary<int, DraftComponent>>();
        }

        var pool = candidates.Distinct().Order().ToList();
        var board = enemies.Distinct().Order().ToList();
        return await cache.GetOrComputeAsync(
            $"champions:draft:enemies:{position}:{string.Join(',', pool)}:{string.Join(',', board)}"
                + $":{scope.Token}:{EloBracket.ResolveToken(eloBracket)}",
            token => ComputeAsync(pool, position, board, scope, eloBracket, token),
            ct);
    }

    private async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<int, DraftComponent>>> ComputeAsync(
        IReadOnlyList<int> pool,
        string position,
        IReadOnlyList<int> board,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
    {
        var window = scope.Window;
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);

        var pairsQuery = db.ChampionOpponentStats
            .AsNoTracking()
            .Where(s => s.TeamPosition == position
                && pool.Contains(s.ChampionId)
                && board.Contains(s.OpponentChampionId)
                && s.OpponentPosition != position);
        var baselineQuery = db.ChampionOpponentBaselineStats.AsNoTracking();
        if (window is not null)
        {
            pairsQuery = pairsQuery.Where(s => window.Contains(s.Patch));
            baselineQuery = baselineQuery.Where(b => window.Contains(b.Patch));
        }

        if (bands is not null)
        {
            pairsQuery = pairsQuery.Where(s => bands.Contains(s.EloBracket));
            baselineQuery = baselineQuery.Where(b => bands.Contains(b.EloBracket));
        }

        var pairs = await pairsQuery
            .GroupBy(s => new { s.ChampionId, s.OpponentChampionId, s.Patch })
            .Select(g => new Row(g.Key.ChampionId, g.Key.OpponentChampionId, g.Key.Patch, g.Sum(s => s.Games), g.Sum(s => s.Wins)))
            .ToListAsync(ct);

        // Every SELF row feeds the cohort intercept; the candidates' own and the
        // enemies' (off our lane) feed the two marginals.
        var baselines = await baselineQuery
            .Where(b => b.Side == OpponentBaselineSide.Self
                || (b.Side == OpponentBaselineSide.Enemy && board.Contains(b.ChampionId) && b.TeamPosition != position))
            .GroupBy(b => new { b.Side, b.ChampionId, Own = b.TeamPosition == position, b.Patch })
            .Select(g => new BaselineRow(g.Key.Side, g.Key.ChampionId, g.Key.Own, g.Key.Patch, g.Sum(b => b.Games), g.Sum(b => b.Wins)))
            .ToListAsync(ct);

        var result = new Dictionary<int, IReadOnlyDictionary<int, DraftComponent>>();
        foreach (var candidate in pool)
        {
            var patches = PatchesFor(candidate, baselines, scope);
            result[candidate] = Deltas(candidate, board, pairs, baselines, patches);
        }

        return result;
    }

    /// <summary>
    /// The current patch when the candidate has <see cref="DraftLaneReader.MinCurrentPatchGames"/>
    /// at our lane there, else the whole window — the lane reader's rule, on this table's own SELF rows.
    /// </summary>
    internal static IReadOnlySet<string>? PatchesFor(int candidate, IReadOnlyList<BaselineRow> baselines, DraftPatchScope scope)
    {
        if (scope.Current is null)
        {
            return null;
        }

        var current = baselines
            .Where(b => b.Side == OpponentBaselineSide.Self && b.ChampionId == candidate && b.Own && b.Patch == scope.Current)
            .Sum(b => b.Games);
        return current >= DraftLaneReader.MinCurrentPatchGames || scope.Previous is null
            ? new HashSet<string> { scope.Current }
            : new HashSet<string> { scope.Current, scope.Previous };
    }

    internal static Dictionary<int, DraftComponent> Deltas(
        int candidate,
        IReadOnlyList<int> board,
        IReadOnlyList<Row> pairs,
        IReadOnlyList<BaselineRow> baselines,
        IReadOnlySet<string>? patches)
    {
        bool InScope(string patch) => patches is null || patches.Contains(patch);

        var self = Sum(baselines.Where(b => b.Side == OpponentBaselineSide.Self && b.ChampionId == candidate && b.Own && InScope(b.Patch)));
        var cohort = Sum(baselines.Where(b => b.Side == OpponentBaselineSide.Self && InScope(b.Patch)));
        var deltas = new Dictionary<int, DraftComponent>();
        if (self.Games < MinBaselineGames || cohort.Games == 0)
        {
            return deltas;
        }

        foreach (var enemy in board)
        {
            var pair = Sum(pairs.Where(p => p.ChampionId == candidate && p.OpponentChampionId == enemy && InScope(p.Patch)));
            var faced = Sum(baselines.Where(b => b.Side == OpponentBaselineSide.Enemy && b.ChampionId == enemy && InScope(b.Patch)));
            if (pair.Games == 0 || faced.Games < MinBaselineGames)
            {
                continue;
            }

            var expected = SynergyMath.ExpectedWinRate(
                (double)self.Wins / self.Games,
                [(double)faced.Wins / faced.Games],
                (double)cohort.Wins / cohort.Games);
            deltas[enemy] = new DraftComponent(((double)pair.Wins / pair.Games) - expected, (int)Math.Min(int.MaxValue, pair.Games));
        }

        return deltas;
    }

    private static (long Games, long Wins) Sum(IEnumerable<Row> rows)
        => rows.Aggregate((Games: 0L, Wins: 0L), (sum, row) => (sum.Games + row.Games, sum.Wins + row.Wins));

    private static (long Games, long Wins) Sum(IEnumerable<BaselineRow> rows)
        => rows.Aggregate((Games: 0L, Wins: 0L), (sum, row) => (sum.Games + row.Games, sum.Wins + row.Wins));

    internal sealed record Row(int ChampionId, int OpponentChampionId, string Patch, int Games, int Wins);

    /// <summary>
    /// One baseline slice, folded to (side, champion, whether its lane is ours, patch).
    /// </summary>
    internal sealed record BaselineRow(string Side, int ChampionId, bool Own, string Patch, int Games, int Wins);
}
