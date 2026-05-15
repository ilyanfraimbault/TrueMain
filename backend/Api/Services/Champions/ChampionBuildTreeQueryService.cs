using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionBuildTreeQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options) : IChampionBuildTreeQueryService
{
    public async Task<ChampionBuildTreeReadModel> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        int maxDepth,
        int minBranchGames,
        CancellationToken ct)
    {
        maxDepth = Math.Clamp(maxDepth, 1, 7);
        minBranchGames = Math.Max(1, minBranchGames);

        var scopeIds = await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(championId, options.Value.QueueId, riotAccountId, patch, platformId, position)
            .Select(scope => scope.Id)
            .ToListAsync(ct);

        // Phase 6.3 — read from the pattern junction. The projector
        // groups runes by (BuildId, RunePageId) and hydrates FirstItemId
        // from BuildDim.BuildItem0, so SelectTopForFirstItem in
        // ChampionRunePageAggregator still picks the right correlated rune
        // per tree root. No FirstItemId pre-filter here either: empty rune
        // pages wouldn't have made it into a pattern row in the first
        // place (the aggregator always pairs them with a build).
        var projection = await ChampionPatternProjector.ProjectAsync(
            db,
            scopeIds,
            ChampionPatternPivot.None,
            ct);

        var builds = projection.Builds
            .Where(build =>
                build.BuildItem0 > 0
                || build.BuildItem1 > 0
                || build.BuildItem2 > 0
                || build.BuildItem3 > 0
                || build.BuildItem4 > 0
                || build.BuildItem5 > 0
                || build.BuildItem6 > 0)
            .ToList();

        var totalGames = builds.Sum(build => build.Games);
        var tree = ChampionBuildTreeBuilder.Build(builds, totalGames, maxDepth, minBranchGames, projection.RunePages);
        var bootsOption = SelectBoots(builds, totalGames);

        return new ChampionBuildTreeReadModel
        {
            ChampionId = championId,
            Patch = patch,
            Position = position,
            RiotAccountId = riotAccountId,
            PlatformId = platformId,
            TotalGames = totalGames,
            Boots = bootsOption,
            Build = tree
        };
    }

    private static ItemSetOptionReadModel? SelectBoots(
        IReadOnlyList<ChampionAggregateBuild> builds,
        int totalGames)
    {
        if (builds.Count == 0 || totalGames <= 0)
        {
            return null;
        }

        return builds
            .Where(build => build.BootsItemId > 0)
            .GroupBy(build => build.BootsItemId)
            .Select(group =>
            {
                var games = group.Sum(build => build.Games);
                return new ItemSetOptionReadModel
                {
                    ItemIds = [group.Key],
                    Games = games,
                    PlayRate = ChampionOptionProjector.ComputeRate(games, totalGames),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(build => build.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.ItemIds[0])
            .FirstOrDefault();
    }
}
