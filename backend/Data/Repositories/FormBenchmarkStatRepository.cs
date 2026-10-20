using Core.Lol.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class FormBenchmarkStatRepository(TrueMainDbContext db) : IFormBenchmarkStatRepository
{
    public async Task AddCountsAsync(
        IReadOnlyCollection<FormBenchmarkCount> counts,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (counts.Count == 0)
        {
            return;
        }

        var patches = counts.Select(count => count.Key.Patch).ToArray();
        var tiers = counts.Select(count => count.Key.Tier.ToRiotName()).ToArray();
        var positions = counts.Select(count => count.Key.Position).ToArray();
        var metrics = counts.Select(count => count.Key.Metric.ToString()).ToArray();
        var buckets = counts.Select(count => count.Key.Bucket).ToArray();
        var values = counts.Select(count => count.Count).ToArray();

        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO form_benchmark_stats
                 ("Id", "Patch", "Tier", "Position", "Metric", "Bucket", "Count", "AggregatedAtUtc")
             SELECT gen_random_uuid(), t.patch, t.tier, t.position, t.metric, t.bucket, t.count, {aggregatedAtUtc}
             FROM unnest({patches}, {tiers}, {positions}, {metrics}, {buckets}, {values})
                 AS t(patch, tier, position, metric, bucket, count)
             ON CONFLICT ("Position", "Patch", "Tier", "Metric", "Bucket") DO UPDATE SET
                 "Count" = form_benchmark_stats."Count" + EXCLUDED."Count",
                 "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
             """,
            ct);
    }
}
