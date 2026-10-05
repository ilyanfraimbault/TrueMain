using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class PaceBenchmarkStatRepository(TrueMainDbContext db) : IPaceBenchmarkStatRepository
{
    public async Task AddCountsAsync(
        IReadOnlyCollection<PaceBenchmarkCount> counts,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (counts.Count == 0)
        {
            return;
        }

        var patches = counts.Select(count => count.Key.Patch).ToArray();
        var tiers = counts.Select(count => count.Key.Tier).ToArray();
        var positions = counts.Select(count => count.Key.Position).ToArray();
        var minutes = counts.Select(count => count.Key.Minute).ToArray();
        var metrics = counts.Select(count => count.Key.Metric.ToString()).ToArray();
        var buckets = counts.Select(count => count.Key.Bucket).ToArray();
        var values = counts.Select(count => count.Count).ToArray();

        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO pace_benchmark_stats
                 ("Id", "Patch", "Tier", "Position", "Minute", "Metric", "Bucket", "Count", "AggregatedAtUtc")
             SELECT gen_random_uuid(), t.patch, t.tier, t.position, t.minute, t.metric, t.bucket, t.count, {aggregatedAtUtc}
             FROM unnest({patches}, {tiers}, {positions}, {minutes}, {metrics}, {buckets}, {values})
                 AS t(patch, tier, position, minute, metric, bucket, count)
             ON CONFLICT ("Position", "Patch", "Tier", "Minute", "Metric", "Bucket") DO UPDATE SET
                 "Count" = pace_benchmark_stats."Count" + EXCLUDED."Count",
                 "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
             """,
            ct);
    }
}
