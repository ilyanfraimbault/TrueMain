using Core.Options;
using Data;
using Data.Ops.Mongo;
using Data.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Health;

public interface IRegionBalanceQueryService
{
    Task<RegionBalanceReadModel> GetAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Measures the region-balance panel of the health cockpit (#1153). Four grouped reads —
/// accounts, active-main accounts, active mains per (platform, champion), matches ingested
/// per platform per day — plus the Ingestor's published coverage configuration, handed to
/// <see cref="RegionBalanceCalculator"/>.
/// </summary>
public sealed class RegionBalanceQueryService(
    TrueMainDbContext db,
    IEffectiveConfigurationStore configurationStore,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IOptions<PipelineHealthOptions> pipelineHealthOptions) : IRegionBalanceQueryService
{
    public async Task<RegionBalanceReadModel> GetAsync(DateTime nowUtc, CancellationToken ct)
    {
        var windowDays = Math.Max(1, pipelineHealthOptions.Value.RegionBalanceWindowDays);

        // Whole UTC days, today included, so the first bucket is never a partial day that
        // would read as a dip.
        var windowStartUtc = DateTime.SpecifyKind(nowUtc.Date.AddDays(-(windowDays - 1)), DateTimeKind.Utc);
        var queueId = (int)mainAnalysisOptions.Value.QueueId;

        var configuration = RegionBalanceCalculator.ReadIngestorConfiguration(
            await configurationStore.GetAllAsync(ct));

        var accounts = await db.RiotAccounts
            .AsNoTracking()
            .GroupBy(account => account.PlatformId)
            .Select(group => new { PlatformId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlatformId, row => row.Count, ct);

        var mainAccounts = await db.MainChampionStats
            .AsNoTracking()
            .Where(stat => stat.IsMain && stat.IsActive)
            .GroupBy(stat => stat.PlatformId)
            .Select(group => new
            {
                PlatformId = group.Key,
                Count = group.Select(stat => stat.Puuid).Distinct().Count()
            })
            .ToDictionaryAsync(row => row.PlatformId, row => row.Count, ct);

        var mains = await ActiveMainCoverageQuery.CountByPlatformAndChampionAsync(db, ct);

        // By ingestion time (CreatedAtUtc), not game time: a backfill of last month's games is
        // this week's spend, and spend is what the allocator balances.
        var daily = await db.Database.SqlQuery<DailyMatchesRow>(
                $"""
                 SELECT
                     "PlatformId",
                     to_char(date_trunc('day', "CreatedAtUtc" AT TIME ZONE 'UTC'), 'YYYY-MM-DD') AS "Day",
                     COUNT(*)::bigint AS "Matches"
                 FROM matches
                 WHERE "QueueId" = {queueId} AND "CreatedAtUtc" >= {windowStartUtc}
                 GROUP BY 1, 2
                 """)
            .ToListAsync(ct);

        return RegionBalanceCalculator.Build(new RegionBalanceInputs
        {
            WindowDays = windowDays,
            WindowStartUtc = windowStartUtc,
            Configuration = configuration,
            MainsByPlatformChampion = mains,
            AccountsByPlatform = accounts,
            ActiveMainAccountsByPlatform = mainAccounts,
            DailyMatches = daily
                .Select(row => new PlatformDailyMatchesReadModel
                {
                    Day = row.Day,
                    PlatformId = row.PlatformId,
                    Matches = row.Matches
                })
                .ToList()
        });
    }

    private sealed record DailyMatchesRow(string PlatformId, string Day, long Matches);
}
