using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;
using TrueMain.Services;
using TrueMain.Services.Ops.Coverage;

namespace TrueMain.UnitTests;

/// <summary>
/// The patch-coverage cache (#2302): a warm call never re-evaluates, a burst of cold calls
/// evaluates once, a failed pass is not re-run straight away, and the entry actually lands in
/// the size-limited shared cache (a sizeless entry is silently dropped there, which would
/// look exactly like the uncached endpoint this exists to fix).
/// </summary>
public sealed class CachedPatchCoverageQueryServiceTests
{
    [Fact]
    public async Task GetAsync_ServesTheCachedPayload_OnAWarmCall()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner();
        var service = new CachedPatchCoverageQueryService(inner, cache);

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
            .Select(_ => new CachedPatchCoverageQueryService(inner, cache).GetAsync(CancellationToken.None))
            .ToList();

        inner.Release();
        var results = await Task.WhenAll(callers);

        inner.Passes.Should().Be(1);
        inner.ReceivedToken.CanBeCanceled.Should().BeFalse("the shared pass runs detached from any caller's token");
        results.Should().AllSatisfy(result => result.Should().BeSameAs(results[0]));
    }

    [Fact]
    public async Task GetAsync_DoesNotReEvaluate_RightAfterAFailedPass()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner(fails: true);
        var service = new CachedPatchCoverageQueryService(inner, cache);

        await FluentActions.Awaiting(() => service.GetAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => service.GetAsync(CancellationToken.None))
            .Should().ThrowAsync<ReadBackoffException>();

        inner.Passes.Should().Be(1);
    }

    private static MemoryCache SizeLimitedCache() => new(new MemoryCacheOptions { SizeLimit = 1024 });

    private sealed class CountingInner(bool gated = false, bool fails = false) : IPatchCoverageQueryService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _passes;

        public int Passes => Volatile.Read(ref _passes);

        public CancellationToken ReceivedToken { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async Task<PatchCoverageReadModel> GetAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _passes);
            ReceivedToken = ct;
            if (gated)
            {
                await _gate.Task;
            }

            if (fails)
            {
                throw new InvalidOperationException("57014: canceling statement due to statement timeout");
            }

            return new PatchCoverageReadModel { Verdict = "servable", Status = "green", EvaluatedAtUtc = DateTime.UtcNow };
        }
    }
}
