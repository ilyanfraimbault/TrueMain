using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionSummariesQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IMemoryCache cache) : IChampionSummariesQueryService
{
    // The directory list is the same payload for every caller of GET /champions
    // and stays valid for the few seconds between ingestor flushes. Caching here
    // means the row-fanning groupby below is paid once per window instead of
    // once per request as the table grows on (account, patch, platform, position).
    private const string SummariesCacheKey = "champions:summaries";
    private static readonly TimeSpan SummariesCacheTtl = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<IReadOnlyList<ChampionSummaryReadModel>>(SummariesCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var summaries = await ComputeAllSummariesAsync(ct);
        cache.Set(SummariesCacheKey, summaries, SummariesCacheTtl);
        return summaries;
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> ComputeAllSummariesAsync(CancellationToken ct)
    {
        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == options.Value.QueueId)
            .Select(scope => new ChampionSummaryRow(
                scope.ChampionId,
                scope.GameVersion,
                scope.Position,
                scope.Games,
                scope.Wins,
                scope.RiotAccountId,
                scope.AggregatedAtUtc))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        return rows
            .GroupBy(row => row.ChampionId)
            .Select(group =>
            {
                var latestPatch = ChampionAggregateScopeResolver.ResolvePatchVersion(
                    group.Select(row => row.GameVersion),
                    requestedPatch: null);
                if (string.IsNullOrEmpty(latestPatch))
                {
                    return null;
                }

                var scoped = group.Where(row => string.Equals(row.GameVersion, latestPatch, StringComparison.Ordinal)).ToList();
                var totalGames = scoped.Sum(row => row.Games);
                var totalWins = scoped.Sum(row => row.Wins);
                var trueMainCount = scoped.Select(row => row.RiotAccountId).Distinct().Count();
                var dominantPosition = ChampionAggregateScopeResolver.ResolveDominantPosition(
                    scoped.Select(row => (row.Position, row.Games)));

                return new ChampionSummaryReadModel
                {
                    ChampionId = group.Key,
                    Games = totalGames,
                    WinRate = totalGames == 0 ? 0 : (double)totalWins / totalGames,
                    TrueMainCount = trueMainCount,
                    Position = dominantPosition,
                    LatestPatchVersion = latestPatch,
                    LastUpdatedAtUtc = scoped.Max(row => row.AggregatedAtUtc)
                };
            })
            .OfType<ChampionSummaryReadModel>()
            .OrderByDescending(summary => summary.Games)
            .ThenBy(summary => summary.ChampionId)
            .ToList();
    }

    private sealed record ChampionSummaryRow(
        int ChampionId,
        string GameVersion,
        string Position,
        int Games,
        int Wins,
        Guid RiotAccountId,
        DateTime AggregatedAtUtc);
}
