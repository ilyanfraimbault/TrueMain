using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;
using TrueMain.Services;
using TrueMain.Services.Ops.Health;

namespace TrueMain.UnitTests;

/// <summary>
/// The Overview's cache (#2301), held to the cockpit's contract (#1427): a warm call never
/// recomputes, a burst of cold calls computes once, the entry lands in the size-limited shared
/// cache, and a failed pass is not re-run straight away.
/// </summary>
public sealed class CachedOverviewQueryServiceTests
{
    [Fact]
    public async Task GetAsync_ServesTheCachedPayload_OnAWarmCall()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner();
        var service = new CachedOverviewQueryService(inner, cache);

        var first = await service.GetAsync(CancellationToken.None);
        var second = await service.GetAsync(CancellationToken.None);

        inner.Passes.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAsync_ComputesOnce_ForConcurrentColdCallers()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner(gated: true);

        var callers = Enumerable
            .Range(0, 8)
            .Select(_ => new CachedOverviewQueryService(inner, cache).GetAsync(CancellationToken.None))
            .ToList();

        inner.Release();
        var results = await Task.WhenAll(callers);

        inner.Passes.Should().Be(1);
        results.Should().AllSatisfy(result => result.Should().BeSameAs(results[0]));
    }

    [Fact]
    public async Task GetAsync_DoesNotCancelTheSharedPass_WhenTheOwnerWalksAway()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner(gated: true);
        using var walkAway = new CancellationTokenSource();

        var owner = new CachedOverviewQueryService(inner, cache).GetAsync(walkAway.Token);
        await walkAway.CancelAsync();
        await FluentActions.Awaiting(() => owner).Should().ThrowAsync<OperationCanceledException>();

        inner.Release();
        var next = await new CachedOverviewQueryService(inner, cache).GetAsync(CancellationToken.None);

        inner.Passes.Should().Be(1);
        inner.ReceivedToken.CanBeCanceled.Should().BeFalse();
        next.TrackedAccounts.Should().Be(42);
    }

    [Fact]
    public async Task GetAsync_DoesNotRecompute_RightAfterAFailedPass()
    {
        using var cache = SizeLimitedCache();
        var inner = new CountingInner(fails: true);
        var service = new CachedOverviewQueryService(inner, cache);

        await FluentActions.Awaiting(() => service.GetAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => service.GetAsync(CancellationToken.None))
            .Should().ThrowAsync<ReadBackoffException>();

        inner.Passes.Should().Be(1);
    }

    [Fact]
    public void PlanRows_ReadsThePlannerEstimate_FromAnExplainJsonPlan()
    {
        const string plan = """
            [{"Plan": {"Node Type": "Index Only Scan", "Relation Name": "matches", "Plan Rows": 242380.4, "Plan Width": 4}}]
            """;

        OverviewQueryService.PlanRows(plan).Should().Be(242_380);
    }

    private static MemoryCache SizeLimitedCache() => new(new MemoryCacheOptions { SizeLimit = 1024 });

    private sealed class CountingInner(bool gated = false, bool fails = false) : IOverviewQueryService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _passes;

        public int Passes => Volatile.Read(ref _passes);

        public CancellationToken ReceivedToken { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async Task<OverviewReadModel> GetAsync(CancellationToken ct)
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

            return new OverviewReadModel { TrackedAccounts = 42, ComputedAtUtc = DateTime.UtcNow };
        }
    }
}
