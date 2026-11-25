using Core.Lol.Pace;
using Core.Lol.Ranking;

namespace Ingestor.Persistence;

public interface IPaceBenchmarkStatRepository
{
    /// <summary>
    /// Adds <paramref name="counts"/> to <c>pace_benchmark_stats</c> in one statement: a bin
    /// that already exists has its count increased, a new one is inserted (#1912). Callers
    /// pre-aggregate, so each key appears once in the batch. The statement locks the rows in
    /// conflict-key order whatever the batch's order, so concurrent folds cannot deadlock on
    /// them (#2448): keep it to one call per transaction, or a second call breaks that order.
    /// </summary>
    Task AddCountsAsync(IReadOnlyCollection<PaceBenchmarkCount> counts, DateTime aggregatedAtUtc, CancellationToken ct);
}

/// <summary>One bin increment of the pace benchmark — the grain of <c>pace_benchmark_stats</c>.</summary>
public readonly record struct PaceBenchmarkKey(
    string Patch,
    RankTier Tier,
    string Position,
    int Minute,
    PaceMetric Metric,
    int Bucket);

public readonly record struct PaceBenchmarkCount(PaceBenchmarkKey Key, long Count);
