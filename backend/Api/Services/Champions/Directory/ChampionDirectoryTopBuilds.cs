using System.Diagnostics;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Builds;

namespace TrueMain.Services.Champions.Directory;

/// <summary>
/// The directory's top-build read: one <see cref="TopBuildReadModel"/> per
/// <c>(champion, position)</c> row, under the same patch, elo and population
/// filters as the summaries it sits beside.
/// </summary>
internal static class ChampionDirectoryTopBuilds
{
    private const string Surface = ChampionSummariesQueryService.Surface;

    /// <summary>
    /// Resolves the dominant <c>(firstItem, primaryKeystone)</c> bucket for
    /// every <c>(champion, position)</c> pair on
    /// <paramref name="activePatch"/>, then computes the consensus item
    /// path for that bucket via <see cref="ChampionBuildPathAnalyzer"/> —
    /// the same tree-walk used to build the "core" path on the champion
    /// detail page, so the path shown on each list row matches the path on
    /// that champion's detail page for the same slice.
    /// </summary>
    public static async Task<IReadOnlyDictionary<(int ChampionId, string Position), TopBuildReadModel>> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        string activePatch,
        IReadOnlyList<string>? bracketBands,
        bool truemainsOnly,
        ILogger logger,
        CancellationToken ct)
    {
        // Mirror the summaries elo filter so the row's shown build matches the
        // slice its WR / PR are computed from (null = ALL, no clause; a
        // non-null set — empty included — restricts, see ResolveFilterOrEmpty).
        var scopeQuery = db.ChampionAggregateScopes.AsNoTracking()
            .Where(scope => scope.QueueId == queueId && scope.GameVersion == activePatch);
        if (bracketBands is not null)
        {
            scopeQuery = scopeQuery.Where(scope => bracketBands.Contains(scope.EloBracket));
        }

        // Same population as the WR / PR beside it, for the same reason.
        if (truemainsOnly)
        {
            scopeQuery = scopeQuery.Where(scope => scope.IsMain);
        }

        var groupedSw = Stopwatch.StartNew();
        var grouped = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Join(
                scopeQuery,
                pattern => pattern.ScopeId,
                scope => scope.Id,
                (pattern, scope) => new
                {
                    scope.ChampionId,
                    scope.Position,
                    pattern.BuildId,
                    pattern.RunePageId,
                    pattern.Games,
                    pattern.Wins,
                })
            .Where(row => row.Position != string.Empty)
            .GroupBy(row => new { row.ChampionId, row.Position, row.BuildId, row.RunePageId })
            .Select(group => new
            {
                group.Key.ChampionId,
                group.Key.Position,
                group.Key.BuildId,
                group.Key.RunePageId,
                Games = group.Sum(row => row.Games),
                Wins = group.Sum(row => row.Wins),
            })
            .ToListAsync(ct);
        groupedSw.Stop();
        logger.LogInformation(
            "{Surface} sql=patterns_join_grouped buckets={Buckets} elapsed={ElapsedMs}ms",
            Surface, grouped.Count, groupedSw.ElapsedMilliseconds);

        if (grouped.Count == 0)
        {
            return new Dictionary<(int, string), TopBuildReadModel>();
        }

        var buildIds = grouped.Select(row => row.BuildId).Distinct().ToList();
        var runeIds = grouped.Select(row => row.RunePageId).Distinct().ToList();

        var dimBuildsSw = Stopwatch.StartNew();
        var dimBuilds = await db.ChampionDimBuilds.AsNoTracking()
            .Where(dim => buildIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        dimBuildsSw.Stop();
        logger.LogInformation(
            "{Surface} sql=dim_builds rows={Rows} elapsed={ElapsedMs}ms",
            Surface, dimBuilds.Count, dimBuildsSw.ElapsedMilliseconds);

        var dimRunesSw = Stopwatch.StartNew();
        var dimRunes = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        dimRunesSw.Stop();
        logger.LogInformation(
            "{Surface} sql=dim_rune_pages rows={Rows} elapsed={ElapsedMs}ms",
            Surface, dimRunes.Count, dimRunesSw.ElapsedMilliseconds);

        var result = new Dictionary<(int ChampionId, string Position), TopBuildReadModel>();

        foreach (var laneGroup in grouped.GroupBy(row => (row.ChampionId, row.Position)))
        {
            // Hydrate the bucket rows with their dim entries. Skip any row
            // whose dim lookup is missing (transient state during ingest) or
            // whose build / rune is malformed.
            var enriched = laneGroup
                .Select(row => new
                {
                    row.Games,
                    row.Wins,
                    Build = dimBuilds.GetValueOrDefault(row.BuildId),
                    Rune = dimRunes.GetValueOrDefault(row.RunePageId),
                })
                .Where(row => row.Build is not null && row.Rune is not null
                    && row.Build.BuildItem0 > 0 && row.Rune.PrimaryKeystoneId > 0)
                .ToList();

            if (enriched.Count == 0)
            {
                continue;
            }

            // Same first-tie ordering as ChampionBuildsQueryService: games
            // desc, firstItemId asc, keystoneId asc — so a champion's list
            // row and its detail page land on the same dominant bucket.
            var topBucket = enriched
                .GroupBy(row => (row.Build!.BuildItem0, row.Rune!.PrimaryKeystoneId))
                .Select(group => new
                {
                    FirstItem = group.Key.BuildItem0,
                    Keystone = group.Key.PrimaryKeystoneId,
                    Games = group.Sum(row => row.Games),
                    Wins = group.Sum(row => row.Wins),
                    Rows = group.ToList(),
                })
                .OrderByDescending(bucket => bucket.Games)
                .ThenBy(bucket => bucket.FirstItem)
                .ThenBy(bucket => bucket.Keystone)
                .First();

            // Consensus item path via the same tree-walk + threshold logic
            // the detail page uses for its core build path.
            var sequences = topBucket.Rows
                .Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                    row.Build!.BuildItem1, row.Build.BuildItem2, row.Build.BuildItem3,
                    row.Build.BuildItem4, row.Build.BuildItem5, row.Build.BuildItem6,
                    row.Games, row.Wins))
                .ToList();
            var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, topBucket.Games);
            var (itemPath, _, _) = ChampionBuildPathAnalyzer.WalkPath(
                tree, topBucket.FirstItem, topBucket.Games, topBucket.Wins);

            // Dominant secondary tree within the top bucket.
            var secondaryStyleId = topBucket.Rows
                .GroupBy(row => row.Rune!.SecondaryStyleId)
                .OrderByDescending(group => group.Sum(row => row.Games))
                .ThenBy(group => group.Key)
                .First().Key;

            result[(laneGroup.Key.ChampionId, laneGroup.Key.Position)] = new TopBuildReadModel
            {
                FirstItemId = topBucket.FirstItem,
                PrimaryKeystoneId = topBucket.Keystone,
                SecondaryStyleId = secondaryStyleId,
                ItemPath = itemPath,
            };
        }

        return result;
    }
}
