using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternAggregatePersister(
    IDbContextFactory<TrueMainDbContext> dbContextFactory)
{
    internal async Task ReplaceAggregatesAsync(
        IReadOnlyCollection<AggregateScopeKey> cleanupScopes,
        IReadOnlyCollection<ChampionPatternAggregate> aggregateRows,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await DeleteExistingAggregatesAsync(db, cleanupScopes, ct);

        db.ChampionPatternAggregates.AddRange(aggregateRows);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task DeleteExistingAggregatesAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<AggregateScopeKey> cleanupScopes,
        CancellationToken ct)
    {
        var cleanupScopeSet = cleanupScopes.ToHashSet();
        if (cleanupScopeSet.Count == 0)
        {
            return;
        }

        var cleanupChampionIds = cleanupScopeSet.Select(scope => scope.ChampionId).Distinct().ToList();
        var cleanupGameVersions = cleanupScopeSet.Select(scope => scope.GameVersion).Distinct().ToList();
        var cleanupPlatformIds = cleanupScopeSet.Select(scope => scope.PlatformId).Distinct().ToList();
        var cleanupQueueIds = cleanupScopeSet.Select(scope => scope.QueueId).Distinct().ToList();

        var aggregatesToDelete = await db.ChampionPatternAggregates
            .Where(aggregate =>
                cleanupChampionIds.Contains(aggregate.ChampionId)
                && cleanupGameVersions.Contains(aggregate.GameVersion)
                && cleanupPlatformIds.Contains(aggregate.PlatformId)
                && cleanupQueueIds.Contains(aggregate.QueueId))
            .ToListAsync(ct);

        db.ChampionPatternAggregates.RemoveRange(
            aggregatesToDelete.Where(aggregate =>
                cleanupScopeSet.Contains(new AggregateScopeKey(
                    aggregate.ChampionId,
                    aggregate.GameVersion,
                    aggregate.PlatformId,
                    aggregate.QueueId))));

        await db.SaveChangesAsync(ct);
    }
}
