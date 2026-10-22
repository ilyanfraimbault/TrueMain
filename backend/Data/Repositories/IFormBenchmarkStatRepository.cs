using Core.Lol.Benchmarks;
using Core.Lol.Ranking;

namespace Data.Repositories;

public interface IFormBenchmarkStatRepository
{
    /// <summary>
    /// Adds <paramref name="counts"/> to <c>form_benchmark_stats</c> in one statement: a bin
    /// that already exists has its count increased, a new one is inserted (#2076). Callers
    /// pre-aggregate, so each key appears once in the batch.
    /// </summary>
    Task AddCountsAsync(IReadOnlyCollection<FormBenchmarkCount> counts, DateTime aggregatedAtUtc, CancellationToken ct);
}

/// <summary>One bin increment of the form benchmark — the grain of <c>form_benchmark_stats</c>.</summary>
public readonly record struct FormBenchmarkKey(
    string Patch,
    RankTier Tier,
    string Position,
    FormMetric Metric,
    int Bucket);

public readonly record struct FormBenchmarkCount(FormBenchmarkKey Key, long Count);
