using Ingestor.Processes.Components.PatternAggregation;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Fills <c>champion_patch_stats</c> — the light per-patch record (#1956) — from the
/// scopes a suite seeded, through the pattern aggregation's own backfill. In production
/// the aggregation keeps the record in step with every scope it writes; a suite that
/// seeds scopes directly bypasses it, so a read on the record (the trend, the lifetime
/// games total, #1957) would find it empty without this call.
/// </summary>
internal static class LightRecordSeed
{
    public static Task<int> BackfillFromScopesAsync(PostgresFixture fixture)
    {
        var factory = new TestDbContextFactory(fixture);
        var persister = new ChampionPatternAggregatePersister(factory, new ChampionDimensionResolver(factory));
        return persister.BackfillPatchStatsIfEmptyAsync(DateTime.UtcNow, CancellationToken.None);
    }
}
