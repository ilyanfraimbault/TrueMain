using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Primary/secondary lane derivation from position share across a player's
/// main champions. Single source of truth for every surface that shows a
/// player's lanes next to their name (leaderboard rows, search results) —
/// mirrors <see cref="MainChampionsPolicy"/> so the views can't drift apart.
/// The share logic itself mirrors the profile's #205 aggregation.
/// </summary>
internal static class MainPositions
{
    /// <summary>
    /// Minimum share of games for a lane to count as meaningful. Used both as
    /// the secondary-lane floor here and as the leaderboard's position-filter
    /// bar (any player who plays the lane at least this often is visible
    /// there), so the two bars stay the same by construction.
    /// </summary>
    public const double MinShare = 0.2d;

    /// <summary>
    /// Fetches the per-champion position breakdowns for the given puuids'
    /// mains and derives each player's primary/secondary lane. Runs on the
    /// caller's context — callers that hydrate concurrently must pass their
    /// own short-lived context (a single DbContext is not thread-safe).
    /// </summary>
    public static async Task<Dictionary<string, LeaderboardPositionsReadModel>> FetchAsync(
        TrueMainDbContext ctx,
        string[] puuids,
        CancellationToken ct)
    {
        if (puuids.Length == 0)
        {
            return new Dictionary<string, LeaderboardPositionsReadModel>();
        }

        // PositionBreakdown is JSONB on the row; EF materialises it as
        // List<PositionStat>, so the share aggregation runs in memory over the
        // requested slice (~25 players × few mains each).
        var rows = await ctx.MainChampionStats
            .AsNoTracking()
            .Where(m => puuids.Contains(m.Puuid) && m.IsMain)
            .Select(m => new { m.Puuid, m.PlayRate, m.ChampionMatches, m.PositionBreakdown })
            .ToListAsync(ct);

        var result = new Dictionary<string, LeaderboardPositionsReadModel>(puuids.Length);
        foreach (var group in rows.GroupBy(r => r.Puuid))
        {
            // Mirror ProfileQueryService.FetchMainsAsync exactly: order by
            // PlayRate desc, ChampionMatches desc and keep only the top mains
            // before summing position shares. Without the cap a player flagged
            // IsMain on more than the policy cap would have lanes aggregated
            // over champions the profile never counts, so the surfaces could
            // show a different primary/secondary for the same player.
            var topMains = group
                .OrderByDescending(r => r.PlayRate)
                .ThenByDescending(r => r.ChampionMatches)
                .Take(MainChampionsPolicy.Cap);
            var positions = Compute(topMains.SelectMany(r => r.PositionBreakdown));
            if (positions is not null)
            {
                result[group.Key] = positions;
            }
        }

        return result;
    }

    // Primary + secondary lane from position share across a player's mains.
    // Sum games per lane across every main, then share = lane games / total
    // games. The primary is the highest-share lane (always shown when the
    // player has any positioned games); the secondary is the next lane down,
    // shown only when its share clears the noise floor — otherwise it would
    // surface a one-off off-role cameo that doesn't reflect how the player
    // actually flexes.
    private static LeaderboardPositionsReadModel? Compute(IEnumerable<PositionStat> breakdown)
    {
        var positionSums = breakdown
            .Where(p => !string.IsNullOrWhiteSpace(p.Position))
            .GroupBy(p => p.Position)
            .Select(g => new { Position = g.Key, Games = g.Sum(x => x.Games) })
            .Where(p => p.Games > 0)
            // Deterministic tiebreak: equal-games lanes would otherwise pick a
            // primary/secondary at random across requests, making the row flap.
            .OrderByDescending(p => p.Games)
            .ThenBy(p => p.Position, StringComparer.Ordinal)
            .ToList();

        if (positionSums.Count == 0)
        {
            return null;
        }

        // Every group cleared the Games > 0 filter and at least one survives, so
        // totalGames is always > 0 here — safe to divide for the share below.
        var totalGames = positionSums.Sum(p => p.Games);

        var primary = positionSums[0];

        string? secondary = null;
        if (positionSums.Count > 1)
        {
            var candidate = positionSums[1];
            var share = (double)candidate.Games / totalGames;
            // Below the primary by construction (ordered desc) and meaningful —
            // the "secondary lane" bar matches the position-filter bar the
            // leaderboard already exposes.
            if (share >= MinShare)
            {
                secondary = candidate.Position;
            }
        }

        return new LeaderboardPositionsReadModel
        {
            Primary = primary.Position,
            Secondary = secondary,
        };
    }
}
