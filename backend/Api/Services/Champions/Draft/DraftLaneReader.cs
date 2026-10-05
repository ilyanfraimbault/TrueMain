using Core.Lol.Draft;
using Core.Lol.Ranking;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Draft;

/// <summary>One champion's record at a lane, against every opponent it met there.</summary>
/// <param name="Games">The champion's games at the lane, every opponent included.</param>
/// <param name="Wins">Of <paramref name="Games"/>, those won.</param>
/// <param name="Versus">
/// Per opponent: win rate into it minus the champion's win rate at the lane, and
/// the games behind it.
/// </param>
/// <param name="LanePhase">Per opponent: lanes won and lost at 15 minutes (decided lanes only).</param>
/// <param name="Patch">The patch the record was read from, or the two-patch window it fell back to.</param>
public sealed record DraftLaneRecord(
    int Games,
    int Wins,
    IReadOnlyDictionary<int, DraftComponent> Versus,
    IReadOnlyDictionary<int, (int Wins, int Losses)> LanePhase,
    string? Patch)
{
    public static readonly DraftLaneRecord Empty = new(
        0, 0, new Dictionary<int, DraftComponent>(), new Dictionary<int, (int, int)>(), null);
}

public interface IDraftLaneReader
{
    /// <summary>Each champion's matchup record at <paramref name="position"/>.</summary>
    Task<IReadOnlyDictionary<int, DraftLaneRecord>> ReadRecordsAsync(
        IReadOnlyCollection<int> championIds,
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct);

    /// <summary>
    /// How often each champion is played at <paramref name="position"/>, as games —
    /// the weight of each possible lane opponent.
    /// </summary>
    Task<IReadOnlyDictionary<int, double>> ReadLaneSharesAsync(
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct);

    /// <summary>
    /// The average win rate of the tracked players at <paramref name="position"/> —
    /// what a champion's own win rate there is measured against.
    /// </summary>
    Task<double> ReadLaneAverageAsync(
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct);
}

/// <summary>
/// The lane reads both draft answers share — picks and bans ask the same two
/// questions of the same tables.
/// </summary>
/// <remarks>
/// <para>
/// Matchups come from <c>champion_matchup_stats</c>, the table the matchup page
/// reads, scoped to the requested bracket. Lane shares come from the
/// <c>ALLY</c> synergy baselines — every champion on a tracked player's team at
/// that lane — for the reason <see cref="LanePriorQueryService"/> reads them: the
/// matchup table only covers champions our population mains, and the opponents
/// we need weighted are everybody's.
/// </para>
/// <para>
/// <b>Patch fallback per champion.</b> A champion's record is read from the
/// current patch when it has <see cref="MinCurrentPatchGames"/> there, and from
/// the current and previous patches summed otherwise; the lane shares likewise
/// against <see cref="MinCurrentPatchLaneGames"/>. Per champion, not per draft:
/// on patch day the popular picks are already measured while a niche one is not.
/// </para>
/// </remarks>
public sealed class DraftLaneReader(TrueMainDbContext db, IChampionReadCache cache) : IDraftLaneReader
{
    /// <summary>A champion's games at the lane on the current patch before its previous patch stops being added.</summary>
    internal const int MinCurrentPatchGames = 200;

    /// <summary>The lane's games on the current patch before the shares stop adding the previous one.</summary>
    internal const int MinCurrentPatchLaneGames = 2_000;

    public async Task<IReadOnlyDictionary<int, DraftLaneRecord>> ReadRecordsAsync(
        IReadOnlyCollection<int> championIds,
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
    {
        if (championIds.Count == 0)
        {
            return new Dictionary<int, DraftLaneRecord>();
        }

        var pool = championIds.Distinct().Order().ToList();
        return await cache.GetOrComputeAsync(
            $"champions:draft:lane-records:{position}:{string.Join(',', pool)}:{scope.Token}"
                + $":{EloBracket.ResolveToken(eloBracket)}",
            token => ComputeRecordsAsync(pool, position, scope, eloBracket, token),
            ct);
    }

    public Task<IReadOnlyDictionary<int, double>> ReadLaneSharesAsync(
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
        => cache.GetOrComputeAsync(
            $"champions:draft:lane-shares:{position}:{scope.Token}:{EloBracket.ResolveToken(eloBracket)}",
            token => ComputeLaneSharesAsync(position, scope, eloBracket, token),
            ct);

    public Task<double> ReadLaneAverageAsync(
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
        => cache.GetOrComputeAsync(
            $"champions:draft:lane-average:{position}:{scope.Token}:{EloBracket.ResolveToken(eloBracket)}",
            token => ComputeLaneAverageAsync(position, scope, eloBracket, token),
            ct);

    /// <summary>
    /// From the <c>SELF</c> synergy baselines — the tracked players' own games at
    /// the lane, the same population <c>champion_matchup_stats</c> counts — over
    /// the whole window: an average over every champion is never thin. 0.5 when
    /// nothing is stored.
    /// </summary>
    private async Task<double> ComputeLaneAverageAsync(
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
    {
        var query = db.ChampionSynergyBaselineStats
            .AsNoTracking()
            .Where(b => b.Side == SynergyBaselineSide.Self && b.TeamPosition == position);

        var window = scope.Window;
        if (window is not null)
        {
            query = query.Where(b => window.Contains(b.Patch));
        }

        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        if (bands is not null)
        {
            query = query.Where(b => bands.Contains(b.EloBracket));
        }

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new { Games = g.Sum(b => (long)b.Games), Wins = g.Sum(b => (long)b.Wins) })
            .FirstOrDefaultAsync(ct);

        return totals is null || totals.Games == 0 ? 0.5d : (double)totals.Wins / totals.Games;
    }

    private async Task<IReadOnlyDictionary<int, DraftLaneRecord>> ComputeRecordsAsync(
        IReadOnlyList<int> pool,
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
    {
        var query = db.ChampionMatchupStats
            .AsNoTracking()
            .Where(m => m.TeamPosition == position && pool.Contains(m.ChampionId));

        var window = scope.Window;
        if (window is not null)
        {
            query = query.Where(m => window.Contains(m.Patch));
        }

        // Null = every band, no clause — the same resolution the matchup page uses.
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        if (bands is not null)
        {
            query = query.Where(m => bands.Contains(m.EloBracket));
        }

        var rows = await query
            .GroupBy(m => new { m.ChampionId, m.OpponentChampionId, m.Patch })
            .Select(g => new MatchupRow(
                g.Key.ChampionId,
                g.Key.OpponentChampionId,
                g.Key.Patch,
                g.Sum(m => m.Games),
                g.Sum(m => m.Wins),
                g.Sum(m => m.LaneWins),
                g.Sum(m => m.LaneLosses)))
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.ChampionId)
            .ToDictionary(group => group.Key, group => BuildRecord(group.ToList(), scope));
    }

    /// <summary>
    /// Folds one champion's rows into its record, on the current patch alone or
    /// on the window when the current patch is thin.
    /// </summary>
    internal static DraftLaneRecord BuildRecord(IReadOnlyList<MatchupRow> rows, DraftPatchScope scope)
    {
        var current = scope.Current is null
            ? rows
            : rows.Where(row => row.Patch == scope.Current).ToList();
        var fallBack = scope.Current is not null
            && scope.Previous is not null
            && current.Sum(row => row.Games) < MinCurrentPatchGames;
        var used = fallBack ? rows : current;

        var games = used.Sum(row => row.Games);
        if (games == 0)
        {
            return DraftLaneRecord.Empty;
        }

        var wins = used.Sum(row => row.Wins);
        var overallRate = (double)wins / games;
        var versus = new Dictionary<int, DraftComponent>();
        var lanePhase = new Dictionary<int, (int Wins, int Losses)>();
        foreach (var opponent in used.GroupBy(row => row.OpponentChampionId))
        {
            var opponentGames = opponent.Sum(row => row.Games);
            if (opponentGames == 0)
            {
                continue;
            }

            versus[opponent.Key] = new DraftComponent(
                ((double)opponent.Sum(row => row.Wins) / opponentGames) - overallRate,
                opponentGames);
            lanePhase[opponent.Key] = (opponent.Sum(row => row.LaneWins), opponent.Sum(row => row.LaneLosses));
        }

        var patch = scope.Current is null
            ? null
            : fallBack ? $"{scope.Current}+{scope.Previous}" : scope.Current;
        return new DraftLaneRecord(games, wins, versus, lanePhase, patch);
    }

    private async Task<IReadOnlyDictionary<int, double>> ComputeLaneSharesAsync(
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        CancellationToken ct)
    {
        var query = db.ChampionSynergyBaselineStats
            .AsNoTracking()
            .Where(b => b.Side == SynergyBaselineSide.Ally && b.TeamPosition == position);

        var window = scope.Window;
        if (window is not null)
        {
            query = query.Where(b => window.Contains(b.Patch));
        }

        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        if (bands is not null)
        {
            query = query.Where(b => bands.Contains(b.EloBracket));
        }

        var rows = await query
            .GroupBy(b => new { b.ChampionId, b.Patch })
            .Select(g => new { g.Key.ChampionId, g.Key.Patch, Games = g.Sum(b => b.Games) })
            .ToListAsync(ct);

        var onCurrent = scope.Current is not null
            && (scope.Previous is null
                || rows.Where(row => row.Patch == scope.Current).Sum(row => row.Games) >= MinCurrentPatchLaneGames);

        return rows
            .Where(row => !onCurrent || row.Patch == scope.Current)
            .GroupBy(row => row.ChampionId)
            .ToDictionary(group => group.Key, group => (double)group.Sum(row => row.Games));
    }

    internal sealed record MatchupRow(
        int ChampionId,
        int OpponentChampionId,
        string Patch,
        int Games,
        int Wins,
        int LaneWins,
        int LaneLosses);
}
