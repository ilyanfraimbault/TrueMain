using Microsoft.Extensions.Caching.Memory;
using TrueMain.Services.Champions;

namespace TrueMain.TestKit;

/// <summary>
/// The two shapes of <see cref="IChampionReadCache"/> a champion query service test
/// needs: a real one over the test's own <see cref="IMemoryCache"/> when the caching is
/// what is being asserted, and a pass-through when it is merely in the way.
/// </summary>
public static class TestChampionReadCache
{
    /// <summary>
    /// A real cache over <paramref name="cache"/>, pinned to one aggregation version so
    /// the test controls staleness rather than the clock.
    /// </summary>
    public static IChampionReadCache Wrapping(IMemoryCache cache)
        => new ChampionReadCache(new FixedStamp(), cache);

    /// <summary>
    /// Runs every read and caches nothing. For tests whose subject is what the service
    /// computes, not how long it remembers it.
    /// </summary>
    public static IChampionReadCache PassThrough() => new NoCache();

    private sealed class FixedStamp : IChampionAggregationStamp
    {
        private static readonly DateTime Stamp = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        public Task<DateTime?> GetLatestAsync(CancellationToken ct) => Task.FromResult<DateTime?>(Stamp);
    }

    private sealed class NoCache : IChampionReadCache
    {
        public Task<T> GetOrComputeAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> compute,
            CancellationToken ct,
            int size = 1)
            => compute(ct);
    }
}
