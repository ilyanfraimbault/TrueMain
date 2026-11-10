using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Matchups;

public interface IChampionMatchupQueryService
{
    /// <summary>
    /// Lists a champion's lane matchups at a position: for every opponent the
    /// champion shared a lane with (same <c>TeamPosition</c>, opposite
    /// <c>TeamId</c>) in the same match, the head-to-head game and win counts,
    /// plus the lane counters behind them and the Wilson bounds a caller ranks on.
    /// Read from <c>champion_matchup_stats</c>.
    /// </summary>
    /// <param name="scope">
    /// The champion side: its lane (required — both lane sides must share it), its patch
    /// (null spans every patch with data) and its elo filter — an exact tier or a
    /// cumulative "X+" threshold; null spans every band.
    /// </param>
    /// <param name="opponentChampionId">
    /// Optional opponent narrowing. When set, only the head-to-head against this
    /// one champion is returned (a single entry, or none) and every games floor is
    /// dropped — a deliberate lookup answers with what exists, from one game up,
    /// and is not a ranked list a thin sample could distort.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The matchups list, ordered by win rate descending. Only opponents holding at least
    /// <see cref="TrueMain.Options.ChampionsListOptions.MinMatchupGames"/> games
    /// <em>and</em> at least
    /// <see cref="TrueMain.Options.ChampionsListOptions.MinMatchupPlayRate"/> of the
    /// champion's total matchup games appear; when none clears the floor the list is
    /// empty (still a 200, never 404). Callers slice best / worst on
    /// <see cref="ChampionMatchupEntry.WinRateLowerBound"/> and
    /// <see cref="ChampionMatchupEntry.WinRateUpperBound"/>, not on this order.
    /// </returns>
    Task<ChampionMatchupsResponse> GetAsync(
        ChampionScope scope,
        int? opponentChampionId,
        CancellationToken ct);
}

/// <summary>
/// Champion lane-matchups query. Every <em>global</em> slice — the leaderboard and
/// the single-opponent search alike — is served from the pre-aggregated
/// <c>champion_matchup_stats</c> table (#606): one indexed read, folded to the
/// requested patch / elo scope. One player's own matchups are a different read
/// (<c>PlayerChampionMatchupQueryService</c>), live, because the aggregate carries no
/// account dimension.
///
/// <para>
/// <b>The search moved off the live join.</b> It used to self-join for its games and
/// wins while reading its lane counters from the aggregate (#976), on the premise
/// that an aggregate "built at floor 10" could not answer a one-game lookup. The
/// rows were never stored with a floor — only the read applied one — so the premise
/// was wrong, and the split cost real correctness: the live join sees the retention
/// window (two patches of <c>match_participants</c>) while the aggregate keeps every
/// patch it ever folded, so the same matchup answered 22 games on the leaderboard
/// and 13 in the search, and rows came back reporting a gold gap averaged over more
/// lanes than the games they were shown next to. Both halves now come from the same
/// rows.
/// </para>
///
/// <para>
/// <b>Two floors on the leaderboard, none on the search.</b> A deliberate lookup
/// answers with whatever games exist, down to one. The leaderboard drops anything
/// under <c>max(MinMatchupGames, MinMatchupPlayRate × total)</c> — see
/// <see cref="ChampionsListOptions.MinMatchupPlayRate"/> for why an absolute floor
/// alone cannot work across champions three orders of magnitude apart in volume.
/// </para>
/// </summary>
public sealed class ChampionMatchupQueryService(
    TrueMainDbContext db,
    IOptions<ChampionsListOptions> championsOptions,
    IChampionReadCache cache)
    : IChampionMatchupQueryService
{
    public Task<ChampionMatchupsResponse> GetAsync(
        ChampionScope scope,
        int? opponentChampionId,
        CancellationToken ct)
        => cache.GetOrComputeAsync(
            $"champions:matchups:{scope.CacheToken()}:{opponentChampionId?.ToString() ?? "any"}",
            token => ComputeAsync(scope, opponentChampionId, token),
            ct);

    private async Task<ChampionMatchupsResponse> ComputeAsync(
        ChampionScope scope,
        int? opponentChampionId,
        CancellationToken ct)
    {
        var position = scope.RequiredPosition;
        var normalizedPatch = scope.Patch;

        // Resolve the elo filter to its bands (null = ALL, no clause).
        var bands = EloBracket.ResolveFilterOrEmpty(scope.EloBracket);

        var matchups = await ReadFromAggregateAsync(
            scope.ChampionId, position, normalizedPatch, opponentChampionId, bands, ct);

        return new ChampionMatchupsResponse
        {
            ChampionId = scope.ChampionId,
            Position = position,
            Patch = normalizedPatch,
            Matchups = matchups,
        };
    }

    private async Task<List<ChampionMatchupEntry>> ReadFromAggregateAsync(
        int championId,
        string position,
        string? normalizedPatch,
        int? opponentChampionId,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        // Rows are stored per (opponent, patch, band) with no floor. Fold to the
        // requested scope — one patch, or every patch summed; the requested elo
        // bands, or every band — then apply the floors on the merged total so the
        // all-patches view floors on the real total, not on any single slice.
        var query = db.ChampionMatchupStats
            .AsNoTracking()
            .Where(s => s.ChampionId == championId && s.TeamPosition == position);
        if (normalizedPatch is not null)
        {
            query = query.Where(s => s.Patch == normalizedPatch);
        }
        if (bands is not null)
        {
            query = query.Where(s => bands.Contains(s.EloBracket));
        }

        // The search narrows to its one opponent in SQL — an indexed seek, and the
        // reason it never pays to materialise the champion's whole opponent field.
        var scopedQuery = opponentChampionId is { } opponent
            ? query.Where(s => s.OpponentChampionId == opponent)
            : query;

        var rows = await scopedQuery
            .GroupBy(s => s.OpponentChampionId)
            .Select(g => new
            {
                Opponent = g.Key,
                Games = g.Sum(x => x.Games),
                Wins = g.Sum(x => x.Wins),
                // Summed over the same folded scope as games/wins, so the lane figure
                // describes the same slice as the row's win rate (#919).
                LaneWins = g.Sum(x => x.LaneWins),
                LaneLosses = g.Sum(x => x.LaneLosses),
                // The gap behind those outcomes (#976), carrying its own denominator:
                // rows folded before it shipped contribute outcomes and no gap, and
                // summing both keeps the average over exactly what was measured.
                GoldDiffSum = g.Sum(x => x.LaneGoldDiffSum),
                GoldDiffGames = g.Sum(x => x.LaneGoldDiffGames),
                // The experience gap behind the same lanes (#1111), on its own
                // denominator for the same reason the gold gap needed one.
                XpDiffSum = g.Sum(x => x.LaneXpDiffSum),
                XpDiffGames = g.Sum(x => x.LaneXpDiffGames),
            })
            // A row can exist with zero games — the lane fold inserts one when it
            // reaches a matchup before its sibling does — and it is not a matchup
            // anybody played. Dropped here rather than left to divide by zero.
            .Where(x => x.Games > 0)
            .ToListAsync(ct);

        // The champion's whole opponent field, before any floor: the denominator the
        // play-rate floor is a share of, and the one that turns a matchup's games into
        // "how often this matchup actually happens".
        //
        // The search read a single row, so it cannot sum its way to that total — it
        // would be dividing the row by itself and calling it 100%. It used to report a
        // play rate of 0 instead, which the matchup page (#1098) shows as a headline
        // figure, so the total is now a second aggregate over the same scope: one
        // indexed SUM, no rows materialised.
        var totalGames = opponentChampionId is null
            ? rows.Sum(x => (long)x.Games)
            : await query.SumAsync(s => (long)s.Games, ct);

        var qualifying = opponentChampionId is null
            ? rows.Where(x => x.Games >= LeaderboardFloor(totalGames))
            : rows;

        return MatchupEntries.ToOrdered(
            qualifying.Select(x => new MatchupRow(
                x.Opponent,
                x.Games,
                x.Wins,
                totalGames,
                new LaneOutcome(x.LaneWins, x.LaneLosses, x.GoldDiffSum, x.GoldDiffGames, x.XpDiffSum, x.XpDiffGames))),
            championsOptions.Value.MinDecidedLaneGames);
    }

    /// <summary>
    /// Games a matchup needs to earn a place on the leaderboard: the larger of the
    /// absolute floor and the champion's own volume times the play-rate floor.
    ///
    /// <para>
    /// The larger, not either alone. The absolute floor is the one that matters on a
    /// champion nobody plays, where a share of a small total is a fraction of a game;
    /// the share is the one that matters on a popular champion, where ten games is
    /// 0.07% of the sample and every such line outranks the real matchups. Rounding
    /// is up, so the share floor is never softened to the game below it.
    /// </para>
    /// </summary>
    private int LeaderboardFloor(long totalGames)
    {
        var settings = championsOptions.Value;
        var shareFloor = settings.MinMatchupPlayRate <= 0d
            ? 0
            : (int)Math.Ceiling(settings.MinMatchupPlayRate * totalGames);

        return Math.Max(settings.MinMatchupGames, shareFloor);
    }
}
