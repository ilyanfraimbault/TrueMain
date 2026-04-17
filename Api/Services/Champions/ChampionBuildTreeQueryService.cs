using Core.Options;
using Data;
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

        var query = db.ChampionPatternAggregates
            .AsNoTracking()
            .Where(aggregate => aggregate.ChampionId == championId && aggregate.QueueId == options.Value.QueueId);

        if (riotAccountId.HasValue)
        {
            query = query.Where(aggregate => aggregate.RiotAccountId == riotAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(patch))
        {
            query = query.Where(aggregate => aggregate.GameVersion == patch);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            query = query.Where(aggregate => aggregate.PlatformId == platformId);
        }

        if (!string.IsNullOrWhiteSpace(position))
        {
            query = query.Where(aggregate => aggregate.Position == position);
        }

        var rows = await query.ToListAsync(ct);
        var buildRows = rows
            .Where(HasBuildPath)
            .ToList();
        var totalGames = buildRows.Sum(row => row.Games);
        var build = ChampionBuildTreeBuilder.Build(buildRows, totalGames, maxDepth, minBranchGames);
        var primaryBuildPath = BuildPrimaryPath(build);
        var correlatedBoots = SelectBootsForPrimaryBuildPath(buildRows, primaryBuildPath, totalGames, maxDepth);

        return new ChampionBuildTreeReadModel
        {
            ChampionId = championId,
            Patch = patch,
            Position = position,
            RiotAccountId = riotAccountId,
            PlatformId = platformId,
            TotalGames = totalGames,
            Boots = correlatedBoots,
            Build = build
        };
    }

    private static bool HasBuildPath(Data.Entities.ChampionPatternAggregate aggregate)
        => aggregate.BuildItem0 > 0 ||
           aggregate.BuildItem1 > 0 ||
           aggregate.BuildItem2 > 0 ||
           aggregate.BuildItem3 > 0 ||
           aggregate.BuildItem4 > 0 ||
           aggregate.BuildItem5 > 0 ||
           aggregate.BuildItem6 > 0;

    private static IReadOnlyList<int> BuildPrimaryPath(IReadOnlyList<ChampionBuildTreeNodeReadModel> build)
    {
        var path = new List<int>();
        var current = build.FirstOrDefault();

        while (current is not null)
        {
            path.Add(current.ItemId);
            current = current.Children.FirstOrDefault();
        }

        return path;
    }

    private static ItemSetOptionReadModel? SelectBootsForPrimaryBuildPath(
        IReadOnlyList<Data.Entities.ChampionPatternAggregate> rows,
        IReadOnlyList<int> primaryBuildPath,
        int totalGames,
        int maxDepth)
    {
        if (rows.Count == 0 || primaryBuildPath.Count == 0 || totalGames <= 0)
        {
            return null;
        }

        var matchingRows = rows
            .Where(row => MatchesPrimaryBuildPath(row, primaryBuildPath, maxDepth))
            .ToList();

        if (matchingRows.Count == 0)
        {
            return null;
        }

        var selectedBoots = matchingRows
            .Where(row => row.BootsItemId > 0)
            .GroupBy(row => row.BootsItemId)
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                return new ItemSetOptionReadModel
                {
                    ItemIds = [group.Key],
                    Games = games,
                    PlayRate = ChampionOptionProjector.ComputeRate(games, totalGames),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.ItemIds[0])
            .FirstOrDefault();

        return selectedBoots;
    }

    private static bool MatchesPrimaryBuildPath(
        Data.Entities.ChampionPatternAggregate row,
        IReadOnlyList<int> primaryBuildPath,
        int maxDepth)
    {
        var rowBuildPath = new[]
        {
            row.BuildItem0,
            row.BuildItem1,
            row.BuildItem2,
            row.BuildItem3,
            row.BuildItem4,
            row.BuildItem5,
            row.BuildItem6
        }
        .Where(itemId => itemId > 0)
        .Take(maxDepth)
        .ToList();

        return rowBuildPath.Count >= primaryBuildPath.Count &&
               rowBuildPath.Take(primaryBuildPath.Count).SequenceEqual(primaryBuildPath);
    }
}
