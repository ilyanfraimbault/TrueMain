using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternAggregatePersister(
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IChampionDimensionResolver dimensionResolver)
{
    /// <summary>
    /// Replaces every scope of <paramref name="slices"/> — and, through the cascade, its
    /// patterns — with <paramref name="scopes"/>, in one transaction. A slice that built
    /// nothing (its player was demoted on a mains-only run, its games no longer qualify)
    /// is simply emptied.
    /// </summary>
    internal async Task ReplaceSlicesAsync(
        int queueId,
        IReadOnlyCollection<AggregateSliceKey> slices,
        IReadOnlyCollection<ChampionAggregateScope> scopes,
        IReadOnlyCollection<PatternIntent> patterns,
        CancellationToken ct)
    {
        // Guard against a drift between the builder's GroupBy key and the
        // scope unique index: if we get here with two scopes sharing the
        // (account, champion, patch, platform, queue, position, bracket) key the
        // insert would explode with a Postgres 23505. Dedup defensively.
        var dedupedScopes = scopes
            .GroupBy(scope => (
                scope.RiotAccountId,
                scope.ChampionId,
                scope.GameVersion,
                scope.PlatformId,
                scope.QueueId,
                scope.Position,
                scope.EloBracket))
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

        // The FK cascade from champion_aggregate_patterns drops the scopes' pattern rows
        // in the same statement. New scopes + patterns then take their place.
        await DeleteSlicesAsync(db, queueId, slices, ct);

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

    private static async Task DeleteSlicesAsync(
        TrueMainDbContext db,
        int queueId,
        IReadOnlyCollection<AggregateSliceKey> slices,
        CancellationToken ct)
    {
        if (slices.Count == 0)
        {
            return;
        }

        var accountIds = slices.Select(slice => slice.RiotAccountId).ToArray();
        var championIds = slices.Select(slice => slice.ChampionId).ToArray();
        var gameVersions = slices.Select(slice => slice.GameVersion).ToArray();
        var platformIds = slices.Select(slice => slice.PlatformId).ToArray();

        // One statement for the whole batch, on the (account, champion, patch, platform,
        // position) index — the slice key is its prefix.
        await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM "champion_aggregate_scopes" AS s
            USING unnest({accountIds}, {championIds}, {gameVersions}, {platformIds})
                AS k("RiotAccountId", "ChampionId", "GameVersion", "PlatformId")
            WHERE s."RiotAccountId" = k."RiotAccountId"
                AND s."ChampionId" = k."ChampionId"
                AND s."GameVersion" = k."GameVersion"
                AND s."PlatformId" = k."PlatformId"
                AND s."QueueId" = {queueId}
            """,
            ct);
    }
}
