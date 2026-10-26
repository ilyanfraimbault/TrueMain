using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Stats;

namespace TrueMain.UnitTests;

/// <summary>
/// The Aggregation panel's cache (#2303): a warm call never re-measures, a burst of cold calls
/// measures once, and the entry actually lands in the size-limited shared cache (an entry
/// without a size is silently dropped there).
/// </summary>
public sealed class CachedAggregationStatsQueryServiceTests
{
    [Fact]
    public async Task GetAsync_ServesTheCachedPayload_OnAWarmCall()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner();
        var service = new CachedAggregationStatsQueryService(inner, cache);

        var first = await service.GetAsync(CancellationToken.None);
        var second = await service.GetAsync(CancellationToken.None);

        inner.Passes.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAsync_MeasuresOnce_ForConcurrentColdCallers()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner(gated: true);

        var callers = Enumerable
            .Range(0, 8)
            .Select(_ => new CachedAggregationStatsQueryService(inner, cache).GetAsync(CancellationToken.None))
            .ToList();

        inner.Release();
        var results = await Task.WhenAll(callers);

        inner.Passes.Should().Be(1);
        results.Should().AllSatisfy(result => result.Should().BeSameAs(results[0]));
    }

    private static MemoryCache SizeLimitedCache() => new(new MemoryCacheOptions { SizeLimit = 1024 });

    private sealed class CountingInner(bool gated = false) : IAggregationStatsQueryService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _passes;

        public int Passes => Volatile.Read(ref _passes);

        public void Release() => _gate.TrySetResult();

        public async Task<AggregationsReadModel> GetAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _passes);
            if (gated)
            {
                await _gate.Task;
            }

            return new AggregationsReadModel { QueueId = 420, MeasuredAtUtc = DateTime.UtcNow };
        }
    }
}
