using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// The dominant build (first item, keystone, secondary tree) per account and
/// champion shown on a leaderboard page. Runs on the caller's context — the
/// leaderboard hydrates concurrently, so it passes a short-lived one.
/// </summary>
internal static class LeaderboardTopBuilds
{
    public static async Task<Dictionary<(string Puuid, int ChampionId), ChampionBuild>> FetchAsync(
        TrueMainDbContext ctx,
        int queueId,
        Dictionary<string, List<LeaderboardTopChampionReadModel>> topChampionsByPuuid,
        Dictionary<string, Guid> accountIdByPuuid,
        CancellationToken ct)
    {
        // Resolve the page's (account, champion) pairs straight from the top-3
        // result — only the champions actually shown get a build, keeping the
        // dim fetches to the page slice (≤ ~75 pairs) rather than every champion
        // each account has aggregated. The reverse map recovers the puuid from
        // the scope's RiotAccountId (aggregates are keyed by account, not puuid).
        var puuidByAccountId = new Dictionary<Guid, string>(accountIdByPuuid.Count);
        var pairs = new HashSet<(Guid AccountId, int ChampionId)>();
        foreach (var (puuid, champions) in topChampionsByPuuid)
        {
            if (!accountIdByPuuid.TryGetValue(puuid, out var accountId))
            {
                continue;
            }

            puuidByAccountId[accountId] = puuid;
            foreach (var champion in champions)
            {
                pairs.Add((accountId, champion.ChampionId));
            }
        }

        if (pairs.Count == 0)
        {
            return new Dictionary<(string, int), ChampionBuild>();
        }

        var accountIds = pairs.Select(pair => pair.AccountId).Distinct().ToList();
        var championIds = pairs.Select(pair => pair.ChampionId).Distinct().ToList();

        // Three sequential round trips (one shared context, so not parallel):
        // this aggregate join, then the build-dim lookup, then the rune-dim
        // lookup. This first query joins patterns to the player's ranked-solo
        // scopes for the shown champions, summing each (account, champion, build,
        // runes) combo across every patch / position. Aggregating over all the
        // player's patches mirrors the per-player build pages — the dominant
        // build is the one the player commits to over time, not just on the live
        // patch. The account×champion id filters over-select the cross product,
        // so the exact pairs are re-checked in memory below.
        var grouped = await ctx.ChampionAggregatePatterns
            .AsNoTracking()
            .Join(
                ctx.ChampionAggregateScopes.AsNoTracking()
                    .Where(scope => scope.QueueId == queueId
                        && accountIds.Contains(scope.RiotAccountId)
                        && championIds.Contains(scope.ChampionId))
                    // Mains only: this is the truemains leaderboard, and since
                    // #1346 the aggregate also holds non-main scopes. Without
                    // this a player's off-main games would count towards the
                    // champion they are ranked on.
                    .Where(scope => scope.IsMain),
                pattern => pattern.ScopeId,
                scope => scope.Id,
                (pattern, scope) => new
                {
                    scope.RiotAccountId,
                    scope.ChampionId,
                    pattern.BuildId,
                    pattern.RunePageId,
                    pattern.Games,
                })
            .GroupBy(row => new { row.RiotAccountId, row.ChampionId, row.BuildId, row.RunePageId })
            .Select(group => new
            {
                group.Key.RiotAccountId,
                group.Key.ChampionId,
                group.Key.BuildId,
                group.Key.RunePageId,
                Games = group.Sum(row => row.Games),
            })
            .ToListAsync(ct);

        // Keep only the (account, champion) pairs the page actually asked for —
        // the SQL filtered each id set independently, so an account that plays
        // champion A and another that plays champion B both pulled rows for A
        // and B; this drops the cross-product leakage.
        var relevant = grouped
            .Where(row => pairs.Contains((row.RiotAccountId, row.ChampionId)))
            .ToList();

        if (relevant.Count == 0)
        {
            return new Dictionary<(string, int), ChampionBuild>();
        }

        var buildIds = relevant.Select(row => row.BuildId).Distinct().ToList();
        var runeIds = relevant.Select(row => row.RunePageId).Distinct().ToList();

        var dimBuilds = await ctx.ChampionDimBuilds.AsNoTracking()
            .Where(dim => buildIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, dim => dim.BuildItem0, ct);
        var dimRunes = await ctx.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, dim => new RunePageDim(dim.PrimaryKeystoneId, dim.SecondaryStyleId), ct);

        var result = new Dictionary<(string Puuid, int ChampionId), ChampionBuild>(relevant.Count);

        foreach (var accountChampion in relevant.GroupBy(row => (row.RiotAccountId, row.ChampionId)))
        {
            if (!puuidByAccountId.TryGetValue(accountChampion.Key.RiotAccountId, out var puuid))
            {
                continue;
            }

            // Hydrate each combo with its dim values, dropping rows whose dim
            // lookup is missing (transient ingest state) or whose first item /
            // keystone is malformed — same guard as LoadTopBuildsAsync.
            var enriched = accountChampion
                .Select(row => new
                {
                    row.Games,
                    FirstItem = dimBuilds.GetValueOrDefault(row.BuildId),
                    Rune = dimRunes.GetValueOrDefault(row.RunePageId),
                })
                .Where(row => row.FirstItem > 0 && row.Rune.PrimaryKeystoneId > 0)
                .ToList();

            if (enriched.Count == 0)
            {
                continue;
            }

            // Dominant (firstItem, keystone) bucket — same tie-break order as
            // LoadTopBuildsAsync (games desc, firstItem asc, keystone asc) so a
            // player's leaderboard cell and their champion page agree.
            var topBucket = enriched
                .GroupBy(row => (FirstItemId: row.FirstItem, KeystoneId: row.Rune.PrimaryKeystoneId))
                .Select(bucket => new
                {
                    FirstItem = bucket.Key.FirstItemId,
                    Keystone = bucket.Key.KeystoneId,
                    Games = bucket.Sum(row => row.Games),
                    Rows = bucket.ToList(),
                })
                .OrderByDescending(bucket => bucket.Games)
                .ThenBy(bucket => bucket.FirstItem)
                .ThenBy(bucket => bucket.Keystone)
                .First();

            // Most-common secondary tree within the winning bucket.
            var secondaryStyleId = topBucket.Rows
                .GroupBy(row => row.Rune.SecondaryStyleId)
                .OrderByDescending(group => group.Sum(row => row.Games))
                .ThenBy(group => group.Key)
                .First().Key;

            result[(puuid, accountChampion.Key.ChampionId)] = new ChampionBuild(
                PrimaryKeystoneId: topBucket.Keystone,
                SecondaryStyleId: secondaryStyleId,
                FirstItemId: topBucket.FirstItem);
        }

        return result;
    }

    // Value type so a missing (puuid, champion) lookup yields all-null build ids
    // via GetValueOrDefault instead of needing a null-reference guard at the
    // call site — null is the contract for "no aggregated build".
    public readonly record struct ChampionBuild(int? PrimaryKeystoneId, int? SecondaryStyleId, int? FirstItemId);

    private readonly record struct RunePageDim(int PrimaryKeystoneId, int SecondaryStyleId);
}
