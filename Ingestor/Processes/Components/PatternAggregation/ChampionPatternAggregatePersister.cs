using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternAggregatePersister(
    IDbContextFactory<TrueMainDbContext> dbContextFactory)
{
    internal async Task ReplaceAggregatesAsync(
        IReadOnlyCollection<AggregateScopeKey> cleanupScopes,
        IReadOnlyCollection<ChampionPatternAggregate> legacyAggregateRows,
        IReadOnlyCollection<ChampionAggregateScope> scopes,
        CancellationToken ct)
    {
        // Guard against a drift between the builder's GroupBy key and the
        // scope unique index: if we get here with two scopes sharing the
        // (account, champion, patch, platform, queue, position) key the
        // insert would explode with a Postgres 23505. Dedup defensively.
        var dedupedScopes = scopes
            .GroupBy(scope => (
                scope.RiotAccountId,
                scope.ChampionId,
                scope.GameVersion,
                scope.PlatformId,
                scope.QueueId,
                scope.Position))
            .Select(group => group.OrderByDescending(scope => scope.Games).First())
            .ToList();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Dual-write window: delete + insert on both the legacy wide table
        // and the new normalised schema. Once the reader side migrates in a
        // follow-up PR we drop the legacy branch and the table.
        await DeleteExistingLegacyAggregatesAsync(db, cleanupScopes, ct);
        await DeleteExistingScopesAsync(db, cleanupScopes, ct);

        db.ChampionPatternAggregates.AddRange(legacyAggregateRows);
        db.ChampionAggregateScopes.AddRange(dedupedScopes);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task DeleteExistingLegacyAggregatesAsync(
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

        var aggregatesToDelete = await db.ChampionPatternAggregates
            .Where(aggregate =>
                championIds.Contains(aggregate.ChampionId)
                && gameVersions.Contains(aggregate.GameVersion)
                && platformIds.Contains(aggregate.PlatformId)
                && queueIds.Contains(aggregate.QueueId))
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
