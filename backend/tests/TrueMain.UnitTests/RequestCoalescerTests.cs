using AwesomeAssertions;
using TrueMain.Services;

namespace TrueMain.UnitTests;

/// <summary>
/// The stampede guard behind the dedication ranking (#870). What it has to get right is
/// narrow and easy to get wrong: one pass per concurrent group, no leak of the entry that
/// tracks it, and no way for one caller's cancellation to take the pass down with it.
/// </summary>
public sealed class RequestCoalescerTests
{
    [Fact]
    public async Task GetOrJoinAsync_RunsOnePass_ForConcurrentCallersOnTheSameKey()
    {
        var coalescer = new RequestCoalescer<int>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passes = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref passes);
            await gate.Task;
            return 42;
        }

        // Ten simultaneous misses — the shape that made ten scoring passes of up to
        // 50 000 accounts before this existed.
        var callers = Enumerable
            .Range(0, 10)
            .Select(_ => coalescer.GetOrJoinAsync("ranking", Factory, CancellationToken.None))
            .ToList();

        gate.SetResult();
        var results = await Task.WhenAll(callers);

        passes.Should().Be(1);
        results.Should().AllSatisfy(result => result.Should().Be(42));
    }

    [Fact]
    public async Task GetOrJoinAsync_KeepsSeparateKeysIndependent()
    {
        var coalescer = new RequestCoalescer<string>();

        var first = await coalescer.GetOrJoinAsync("a", () => Task.FromResult("a"), CancellationToken.None);
        var second = await coalescer.GetOrJoinAsync("b", () => Task.FromResult("b"), CancellationToken.None);

        first.Should().Be("a");
        second.Should().Be("b");
    }

    [Fact]
    public async Task GetOrJoinAsync_RunsAgain_OnceThePreviousPassHasFinished()
    {
        // The coalescer is not a cache: it collapses concurrent work and then gets out
        // of the way, so the next miss after a TTL expiry recomputes rather than being
        // served a stale shared result for ever.
        var coalescer = new RequestCoalescer<int>();
        var passes = 0;

        await coalescer.GetOrJoinAsync("k", () => Task.FromResult(Interlocked.Increment(ref passes)), CancellationToken.None);
        await coalescer.GetOrJoinAsync("k", () => Task.FromResult(Interlocked.Increment(ref passes)), CancellationToken.None);

        passes.Should().Be(2);
    }

    [Fact]
    public async Task GetOrJoinAsync_ReleasesTheKey_EvenWhenEveryCallerCancels()
    {
        // If cleanup were left to the callers' finally blocks, a group that all walked
        // away would strand the entry — holding its result (a whole ranking) for ever
        // and blocking every later request from recomputing.
        var coalescer = new RequestCoalescer<int>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = new CancellationTokenSource();
        var caller = coalescer.GetOrJoinAsync("k", async () => { await gate.Task; return 1; }, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await caller);

        // The pass itself survives the abandoning caller, then releases its slot.
        gate.SetResult();
        await WaitUntilAsync(() => coalescer.InFlightCount == 0);
        coalescer.InFlightCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrJoinAsync_LetsOneCallerCancelWithoutFailingTheOthers()
    {
        var coalescer = new RequestCoalescer<int>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = new CancellationTokenSource();
        var abandoning = coalescer.GetOrJoinAsync("k", async () => { await gate.Task; return 7; }, cts.Token);
        var waiting = coalescer.GetOrJoinAsync("k", () => Task.FromResult(0), CancellationToken.None);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await abandoning);

        gate.SetResult();
        (await waiting).Should().Be(7, "the second caller joined the first caller's pass and must still get its result");
    }

    [Fact]
    public async Task GetOrJoinAsync_PropagatesTheFailure_AndDoesNotCacheIt()
    {
        var coalescer = new RequestCoalescer<int>();
        var attempts = 0;

        Task<int> Failing()
        {
            Interlocked.Increment(ref attempts);
            return Task.FromException<int>(new InvalidOperationException("scan failed"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coalescer.GetOrJoinAsync("k", Failing, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coalescer.GetOrJoinAsync("k", Failing, CancellationToken.None));

        // A failed pass must not pin the key: the next request has to be allowed to try.
        attempts.Should().Be(2);
        coalescer.InFlightCount.Should().Be(0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
