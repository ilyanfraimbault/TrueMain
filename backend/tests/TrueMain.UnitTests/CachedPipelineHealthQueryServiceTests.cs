using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Health;

namespace TrueMain.UnitTests;

/// <summary>
/// The cockpit's cache (#1427). What it has to get right: a warm call never re-evaluates, a
/// burst of cold calls evaluates once, and the entry actually lands in the size-limited shared
/// cache (an entry without a size is silently dropped there, which would look exactly like
/// the slow endpoint this exists to fix).
/// </summary>
public sealed class CachedPipelineHealthQueryServiceTests
{
    [Fact]
    public async Task GetAsync_ServesTheCachedPayload_OnAWarmCall()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner();
        var service = new CachedPipelineHealthQueryService(inner, cache);

        var first = await service.GetAsync(CancellationToken.None);
        var second = await service.GetAsync(CancellationToken.None);

        inner.Passes.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAsync_EvaluatesOnce_ForConcurrentColdCallers()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner(gated: true);

        var callers = Enumerable
            .Range(0, 8)
            .Select(_ => new CachedPipelineHealthQueryService(inner, cache).GetAsync(CancellationToken.None))
            .ToList();

        inner.Release();
        var results = await Task.WhenAll(callers);

        inner.Passes.Should().Be(1);
        results.Should().AllSatisfy(result => result.Should().BeSameAs(results[0]));
    }

    [Fact]
    public async Task GetAsync_DoesNotCancelTheSharedPass_WhenOneCallerWalksAway()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner(gated: true);
        using var walkAway = new CancellationTokenSource();

        var owner = new CachedPipelineHealthQueryService(inner, cache).GetAsync(CancellationToken.None);
        var joiner = new CachedPipelineHealthQueryService(inner, cache).GetAsync(walkAway.Token);

        await walkAway.CancelAsync();
        await FluentActions.Awaiting(() => joiner).Should().ThrowAsync<OperationCanceledException>();

        inner.Release();
        var result = await owner;

        inner.Passes.Should().Be(1);
        inner.ReceivedToken.CanBeCanceled.Should().BeFalse();
        (await new CachedPipelineHealthQueryService(inner, cache).GetAsync(CancellationToken.None))
            .Should().BeSameAs(result);
    }

    private static MemoryCache SizeLimitedCache() => new(new MemoryCacheOptions { SizeLimit = 1024 });

    private sealed class CountingInner(bool gated = false) : IPipelineHealthQueryService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _passes;

        public int Passes => Volatile.Read(ref _passes);

        public CancellationToken ReceivedToken { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _passes);
            ReceivedToken = ct;
            if (gated)
            {
                await _gate.Task;
            }

            return new PipelineHealthReadModel { Status = "ok", Headline = "All good.", EvaluatedAtUtc = DateTime.UtcNow };
        }
    }
}
