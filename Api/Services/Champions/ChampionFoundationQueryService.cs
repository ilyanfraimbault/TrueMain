using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionFoundationQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options) : IChampionFoundationQueryService
{
    public async Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct)
    {
        var query = db.ChampionPatternAggregates
            .AsNoTracking()
            .Where(aggregate => aggregate.ChampionId == championId && aggregate.QueueId == options.Value.QueueId);

        if (riotAccountId.HasValue)
        {
            query = query.Where(aggregate => aggregate.RiotAccountId == riotAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            query = query.Where(aggregate => aggregate.PlatformId == platformId);
        }

        if (!string.IsNullOrWhiteSpace(position))
        {
            query = query.Where(aggregate => aggregate.Position == position);
        }

        var aggregateRows = await query.ToListAsync(ct);

        if (aggregateRows.Count == 0)
        {
            return null;
        }

        var selectedPatchVersion = ChampionAggregateScopeResolver.ResolvePatchVersion(aggregateRows, patch);

        if (string.IsNullOrWhiteSpace(selectedPatchVersion))
        {
            return null;
        }

        var patchRows = aggregateRows
            .Where(aggregate => string.Equals(aggregate.GameVersion, selectedPatchVersion, StringComparison.Ordinal))
            .ToList();

        if (patchRows.Count == 0)
        {
            return null;
        }

        var effectivePosition = string.IsNullOrWhiteSpace(position)
            ? ChampionAggregateScopeResolver.ResolveDominantPosition(patchRows)
            : position;

        var scopedRows = string.IsNullOrWhiteSpace(effectivePosition)
            ? patchRows
            : patchRows
                .Where(aggregate => string.Equals(aggregate.Position, effectivePosition, StringComparison.Ordinal))
                .ToList();

        if (scopedRows.Count == 0)
        {
            return null;
        }

        return new ChampionFoundationReadModel
        {
            Summary = BuildSummary(championId, selectedPatchVersion, scopedRows),
            Advanced = ChampionOptionProjector.BuildAdvancedDetails(scopedRows)
        };
    }

    private static ChampionSummaryReadModel BuildSummary(
        int championId,
        string latestPatchVersion,
        IReadOnlyCollection<ChampionPatternAggregate> rows)
    {
        var totalGames = rows.Sum(row => row.Games);
        var totalWins = rows.Sum(row => row.Wins);
        var trueMainCount = rows
            .Select(row => row.RiotAccountId)
            .Distinct()
            .Count();
        var position = ChampionAggregateScopeResolver.ResolveDominantPosition(rows);

        return new ChampionSummaryReadModel
        {
            ChampionId = championId,
            Games = totalGames,
            WinRate = ChampionOptionProjector.ComputeRate(totalWins, totalGames),
            TrueMainCount = trueMainCount,
            Position = position,
            LatestPatchVersion = latestPatchVersion,
            LastUpdatedAtUtc = rows.Max(row => row.AggregatedAtUtc)
        };
    }
}
