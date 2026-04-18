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

        var buildRows = await query
            .Where(aggregate =>
                aggregate.BuildItem0 > 0
                || aggregate.BuildItem1 > 0
                || aggregate.BuildItem2 > 0
                || aggregate.BuildItem3 > 0
                || aggregate.BuildItem4 > 0
                || aggregate.BuildItem5 > 0
                || aggregate.BuildItem6 > 0)
            .ToListAsync(ct);
        var totalGames = buildRows.Sum(row => row.Games);
        var build = ChampionBuildTreeBuilder.Build(buildRows, totalGames, maxDepth, minBranchGames);
        var correlatedBoots = SelectBoots(buildRows, totalGames);

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

    private static ItemSetOptionReadModel? SelectBoots(
        IReadOnlyList<Data.Entities.ChampionPatternAggregate> rows,
        int totalGames)
    {
        if (rows.Count == 0 || totalGames <= 0)
        {
            return null;
        }

        var selectedBoots = rows
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
}
