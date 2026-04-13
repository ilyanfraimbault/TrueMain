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
        var effectivePosition = position;

        if (string.IsNullOrWhiteSpace(effectivePosition))
        {
            effectivePosition = ChampionAggregateScopeResolver.ResolveDominantPosition(rows);

            if (!string.IsNullOrWhiteSpace(effectivePosition))
            {
                rows = rows
                    .Where(aggregate => string.Equals(aggregate.Position, effectivePosition, StringComparison.Ordinal))
                    .ToList();
            }
        }

        var buildRows = rows
            .Where(HasBuildPath)
            .ToList();
        var totalGames = buildRows.Sum(row => row.Games);
        var build = ChampionBuildTreeBuilder.Build(buildRows, totalGames, maxDepth, minBranchGames);

        return new ChampionBuildTreeReadModel
        {
            ChampionId = championId,
            Patch = patch,
            Position = effectivePosition,
            RiotAccountId = riotAccountId,
            PlatformId = platformId,
            TotalGames = totalGames,
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
}
