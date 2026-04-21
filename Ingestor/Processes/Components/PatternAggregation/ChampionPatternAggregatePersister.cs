using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternAggregatePersister(
    IDbContextFactory<TrueMainDbContext> dbContextFactory)
{
    internal async Task ReplaceAggregatesAsync(
        IReadOnlyCollection<AggregateScopeKey> cleanupScopes,
        IReadOnlyCollection<ChampionAggregateScope> scopes,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await DeleteExistingScopesAsync(db, cleanupScopes, ct);

        db.ChampionAggregateScopes.AddRange(scopes);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task DeleteExistingScopesAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<AggregateScopeKey> cleanupScopes,
        CancellationToken ct)
    {
        var cleanupScopeSet = cleanupScopes.ToHashSet();
        if (cleanupScopeSet.Count == 0)
        {
            return;
        }

        var championIds = cleanupScopeSet.Select(scope => scope.ChampionId).Distinct().ToList();
        var gameVersions = cleanupScopeSet.Select(scope => scope.GameVersion).Distinct().ToList();
        var platformIds = cleanupScopeSet.Select(scope => scope.PlatformId).Distinct().ToList();
        var queueIds = cleanupScopeSet.Select(scope => scope.QueueId).Distinct().ToList();

        var scopesToDelete = await db.ChampionAggregateScopes
            .Where(scope =>
                championIds.Contains(scope.ChampionId)
                && gameVersions.Contains(scope.GameVersion)
                && platformIds.Contains(scope.PlatformId)
                && queueIds.Contains(scope.QueueId))
            .ToListAsync(ct);

        db.ChampionAggregateScopes.RemoveRange(
            scopesToDelete.Where(scope =>
                cleanupScopeSet.Contains(new AggregateScopeKey(
                    scope.ChampionId,
                    scope.GameVersion,
                    scope.PlatformId,
                    scope.QueueId))));

        await db.SaveChangesAsync(ct);
    }
}
