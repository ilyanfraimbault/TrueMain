using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternAggregatePersister(
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IChampionDimensionResolver dimensionResolver)
{
    internal async Task ReplaceAggregatesAsync(
        IReadOnlyCollection<AggregateScopeKey> cleanupScopes,
        IReadOnlyCollection<ChampionAggregateScope> scopes,
        IReadOnlyCollection<PatternIntent> patterns,
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
        var keptScopeIds = dedupedScopes.Select(scope => scope.Id).ToHashSet();
        var keptPatterns = patterns.Where(intent => keptScopeIds.Contains(intent.ScopeId)).ToList();

        // Resolve dimension IDs ahead of the transaction. Dim tables are
        // append-only globally and idempotent under UNIQUE; doing the
        // get-or-create outside the scope/pattern transaction means a roll
        // back on the inserts below leaves the dim rows in place
        // harmlessly — the next run reuses them.
        var resolution = keptPatterns.Count > 0
            ? await dimensionResolver.ResolveAsync(keptPatterns, ct)
            : EmptyResolution;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Delete existing scopes for the cleanup keys; the FK cascade from
        // PR 6.1 drops the scope's pattern rows in the same statement. New
        // scopes + patterns then take their place.
        await DeleteExistingScopesAsync(db, cleanupScopes, ct);

        db.ChampionAggregateScopes.AddRange(dedupedScopes);
        await db.SaveChangesAsync(ct);

        if (keptPatterns.Count > 0)
        {
            db.ChampionAggregatePatterns.AddRange(BuildPatternRows(keptPatterns, resolution));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static IEnumerable<ChampionAggregatePattern> BuildPatternRows(
        IReadOnlyCollection<PatternIntent> patterns,
        DimensionResolution resolution)
        => patterns.Select(intent => new ChampionAggregatePattern
        {
            ScopeId = intent.ScopeId,
            BuildId = resolution.Builds[intent.Build],
            RunePageId = resolution.RunePages[intent.RunePage],
            SkillOrderId = resolution.SkillOrders[intent.SkillOrderKey],
            SpellPairId = resolution.SpellPairs[intent.SpellPair],
            StarterItemsId = resolution.StarterItems[intent.StarterItemsKey],
            Games = intent.Games,
            Wins = intent.Wins
        });

    private static readonly DimensionResolution EmptyResolution = new(
        new Dictionary<BuildDimensionContent, Guid>(),
        new Dictionary<RunePageDimensionContent, Guid>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal),
        new Dictionary<SpellPairDimensionContent, Guid>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal));

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
