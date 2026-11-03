using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops;
using TrueMain.Services.Ops.DataQuality;

namespace TrueMain.UnitTests;

/// <summary>
/// The detector suite's cache (#2305): a warm call never re-runs the five detectors, the
/// on-demand freshness breakdown is never cached, and every warm payload shares one refresher.
/// </summary>
public sealed class CachedDataQualityDetectorsQueryServiceTests
{
    [Fact]
    public async Task GetDetectorsAsync_ServesTheCachedPayload_OnAWarmCall()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });
        var inner = new CountingInner();

        var first = await new CachedDataQualityDetectorsQueryService(inner, cache).GetDetectorsAsync(CancellationToken.None);
        var second = await new CachedDataQualityDetectorsQueryService(inner, cache).GetDetectorsAsync(CancellationToken.None);

        inner.DetectorPasses.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAggregateFreshnessAsync_IsNeverCached()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });
        var inner = new CountingInner();
        var service = new CachedDataQualityDetectorsQueryService(inner, cache);

        await service.GetAggregateFreshnessAsync(CancellationToken.None);
        await service.GetAggregateFreshnessAsync(CancellationToken.None);

        inner.FreshnessPasses.Should().Be(2);
    }

    [Fact]
    public void AddWarmPayload_RegistersOneRefresher_ForEveryPayload()
    {
        var services = new ServiceCollection();

        services.AddTrueMainDataQuality();
        services.AddWarmPayload(
            CachedDataQualityDetectorsQueryService.Payload,
            _ => Task.FromResult(new DataQualityDetectorsReadModel()));

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(WarmPayloadTarget)).Should().Be(2);
    }

    private sealed class CountingInner : IDataQualityDetectorsQueryService
    {
        private int _detectorPasses;
        private int _freshnessPasses;

        public int DetectorPasses => Volatile.Read(ref _detectorPasses);

        public int FreshnessPasses => Volatile.Read(ref _freshnessPasses);

        public Task<DataQualityDetectorsReadModel> GetDetectorsAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _detectorPasses);
            return Task.FromResult(new DataQualityDetectorsReadModel { EvaluatedAtUtc = DateTime.UtcNow });
        }

        public Task<AggregateFreshnessReadModel> GetAggregateFreshnessAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _freshnessPasses);
            return Task.FromResult(new AggregateFreshnessReadModel());
        }
    }
}
