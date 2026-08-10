using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services.Champions;

/// <summary>
/// The ordered core item path of each build slice of a champion — the same lists
/// <see cref="ChampionBuildsQueryService"/> renders as the build tabs' cores, keyed
/// by the same <c>(BuildItem0, PrimaryKeystoneId)</c> pair it groups tabs by.
///
/// It exists because the powerspike read needs to answer "which items belong to the
/// build the user is looking at, and in what order", and that question is only
/// answerable from the pattern aggregates: the powerspike event rows are keyed on
/// the build, but their item set is whatever each game completed, so on its own the
/// event table cannot tell a core item from a situational one (#1021).
///
/// Resolution deliberately mirrors the builds read step for step — same scope
/// loader, same pruned tree and greedy walk — because the two lists are shown on the
/// same card and any divergence reads as a bug.
///
/// Every build of the slice is resolved in one pass and cached, because the champion
/// page mounts all of its build panels at once (<c>unmount-on-hide="false"</c>): one
/// query per (champion, position, patch, bracket) instead of one per open tab.
/// </summary>
internal static class ChampionCoreBuildPathResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Returns the core item ids in build order (index 0 is
    /// <paramref name="buildFirstItemId"/>), or an empty list when the slice has no
    /// aggregate rows for that build. A caller that gets an empty list must not fall
    /// back to an unordered item set: no path means we cannot say which items are the
    /// build's, and answering with the wrong ones is what this resolver exists to
    /// prevent.
    /// </summary>
    public static async Task<IReadOnlyList<int>> ResolveAsync(
        TrueMainDbContext db,
        IMemoryCache cache,
        int queueId,
        int championId,
        string position,
        string? patch,
        IReadOnlyCollection<string>? bands,
        string bracketToken,
        int buildFirstItemId,
        int buildKeystoneId,
        CancellationToken ct)
    {
        var paths = await ResolveAllAsync(
            db, cache, queueId, championId, position, patch, bands, bracketToken, ct);

        return paths.GetValueOrDefault((buildFirstItemId, buildKeystoneId), []);
    }

    private static async Task<IReadOnlyDictionary<(int FirstItemId, int KeystoneId), IReadOnlyList<int>>> ResolveAllAsync(
        TrueMainDbContext db,
        IMemoryCache cache,
        int queueId,
        int championId,
        string position,
        string? patch,
        IReadOnlyCollection<string>? bands,
        string bracketToken,
        CancellationToken ct)
    {
        var cacheKey = $"champions:corebuildpaths:{championId}:{position}:{patch ?? "all"}:{bracketToken}";
        if (cache.TryGetValue<IReadOnlyDictionary<(int, int), IReadOnlyList<int>>>(cacheKey, out var cached)
            && cached is not null)
        {
            return cached;
        }

        var resolved = await LoadAsync(db, queueId, championId, position, patch, bands, ct);
        cache.Set(cacheKey, resolved, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return resolved;
    }

    private static async Task<IReadOnlyDictionary<(int, int), IReadOnlyList<int>>> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        int championId,
        string position,
        string? patch,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        // The caller hands over the already-resolved bands rather than the raw
        // filter, so the paths and the events they scope can never be read on two
        // different bracket sets.
        var scopes = await ChampionScopeLoader.LoadAsync(
            db, queueId, championId, patch, position, ct, eloBrackets: bands);
        if (scopes is null || scopes.Count == 0)
        {
            return new Dictionary<(int, int), IReadOnlyList<int>>();
        }

        var scopeIds = scopes.Select(s => s.Id).ToList();

        var rows = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Where(pattern => scopeIds.Contains(pattern.ScopeId))
            .Join(
                db.ChampionDimBuilds.AsNoTracking(),
                pattern => pattern.BuildId,
                build => build.Id,
                (pattern, build) => new { Pattern = pattern, Build = build })
            .Join(
                db.ChampionDimRunePages.AsNoTracking(),
                joined => joined.Pattern.RunePageId,
                rune => rune.Id,
                (joined, rune) => new
                {
                    joined.Build.BuildItem0,
                    joined.Build.BuildItem1,
                    joined.Build.BuildItem2,
                    joined.Build.BuildItem3,
                    joined.Build.BuildItem4,
                    joined.Build.BuildItem5,
                    joined.Build.BuildItem6,
                    rune.PrimaryKeystoneId,
                    joined.Pattern.Games,
                    joined.Pattern.Wins
                })
            .Where(row => row.BuildItem0 > 0 && row.PrimaryKeystoneId > 0)
            .ToListAsync(ct);

        var paths = new Dictionary<(int, int), IReadOnlyList<int>>();

        foreach (var group in rows.GroupBy(row => (row.BuildItem0, row.PrimaryKeystoneId)))
        {
            var sequences = group
                .Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                    row.BuildItem1, row.BuildItem2, row.BuildItem3,
                    row.BuildItem4, row.BuildItem5, row.BuildItem6,
                    row.Games, row.Wins))
                .ToList();

            var sliceGames = group.Sum(row => row.Games);
            var sliceWins = group.Sum(row => row.Wins);

            var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames);
            var (itemIds, _, _) = ChampionBuildPathAnalyzer.WalkPath(
                tree, group.Key.BuildItem0, sliceGames, sliceWins);

            paths[group.Key] = itemIds;
        }

        return paths;
    }
}
