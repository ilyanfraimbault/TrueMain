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
    // on a given patch and stays valid for the few seconds between ingestor
    // flushes. Caching keyed on the resolved patch means the row-fanning groupby
    // below is paid once per (patch, window) instead of once per request.
    private static readonly TimeSpan SummariesCacheTtl = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(string? patch, CancellationToken ct)
    {
        var cacheKey = $"champions:summaries:{patch ?? "latest"}";
        if (cache.TryGetValue<IReadOnlyList<ChampionSummaryReadModel>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var summaries = await ComputeAllSummariesAsync(patch, ct);
        cache.Set(cacheKey, summaries, SummariesCacheTtl);
        return summaries;
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> ComputeAllSummariesAsync(string? requestedPatch, CancellationToken ct)
    {
        // Resolve the active patch first so the row pull below can apply the
        // GameVersion filter in SQL. Loading every aggregate row for the queue
        // before filtering in-memory would re-scan the whole table on every
        // cache miss — fine when only the latest patch matters but wasteful
        // once historical patches are reachable through ?patch=.
        var activePatch = requestedPatch;
        if (string.IsNullOrEmpty(activePatch))
        {
            var distinctPatches = await db.ChampionAggregateScopes
                .AsNoTracking()
                .Where(scope => scope.QueueId == options.Value.QueueId)
                .Select(scope => scope.GameVersion)
                .Distinct()
                .ToListAsync(ct);
            activePatch = ChampionAggregateScopeResolver.ResolvePatchVersion(distinctPatches, requestedPatch: null);
        }
        if (string.IsNullOrEmpty(activePatch))
        {
            return [];
        }

        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == options.Value.QueueId)
            .Where(scope => scope.GameVersion == activePatch)
            .Select(scope => new ChampionSummaryRow(
                scope.ChampionId,
                scope.GameVersion,
                scope.Position,
                scope.Games,
                scope.Wins,
                scope.RiotAccountId,
                scope.AggregatedAtUtc))
            .ToListAsync(ct);

        var scoped = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Position))
            .ToList();

        if (scoped.Count == 0)
        {
            return [];
        }

        // Pre-computed denominators reused across every group.
        // - championTotals: this champion's games across all lanes  → LanePlayRate
        // - laneTotals    : all games on this lane across champions → PickRate
        var championTotals = scoped
            .GroupBy(row => row.ChampionId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Games));
        var laneTotals = scoped
            .GroupBy(row => row.Position, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Games), StringComparer.Ordinal);

        return scoped
            .GroupBy(row => (row.ChampionId, row.Position))
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                var wins = group.Sum(row => row.Wins);
                var championTotal = championTotals.GetValueOrDefault(group.Key.ChampionId);
                var laneTotal = laneTotals.GetValueOrDefault(group.Key.Position);

                return new ChampionSummaryReadModel
                {
                    ChampionId = group.Key.ChampionId,
                    Games = games,
                    Wins = wins,
                    WinRate = games == 0 ? 0 : (double)wins / games,
                    PickRate = laneTotal == 0 ? 0 : (double)games / laneTotal,
                    LanePlayRate = championTotal == 0 ? 0 : (double)games / championTotal,
                    TrueMainCount = group.Select(row => row.RiotAccountId).Distinct().Count(),
                    Position = group.Key.Position,
                    PatchVersion = activePatch,
                    LastUpdatedAtUtc = group.Max(row => row.AggregatedAtUtc)
                };
            })
            .OrderByDescending(summary => summary.PickRate)
            .ThenBy(summary => summary.ChampionId)
            .ThenBy(summary => summary.Position, StringComparer.Ordinal)
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
