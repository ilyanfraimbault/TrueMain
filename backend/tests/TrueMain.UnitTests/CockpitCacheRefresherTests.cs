using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TrueMain.Services;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The cockpit's warm cache (#2305): a read never waits on a value that exists, the refresher
/// recomputes only what was read recently and only shortly before it goes stale, one payload at
/// a time, and a failed refresh keeps the last good value.
/// </summary>
public sealed class CockpitCacheRefresherTests : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetAsync_ServesAStaleValue_WithoutRecomputing()
    {
        var source = new Source();
        var payload = NewPayload();

        var first = await ReadAsync(payload, source);
        _clock.Advance(Ttl * 3);
        var second = await ReadAsync(payload, source);

        source.Passes.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task RefreshNext_LeavesAPayloadNobodyReadAlone()
    {
        var source = new Source();
        var payload = NewPayload();
        using var refresher = NewRefresher((payload, source));

        _clock.Advance(Ttl);
        (await refresher.RefreshNextAsync()).Should().BeNull();

        source.Passes.Should().Be(0);
    }

    [Fact]
    public async Task RefreshNext_RecomputesARecentlyReadValue_BeforeItGoesStale()
    {
        var source = new Source();
        var payload = NewPayload();
        using var refresher = NewRefresher((payload, source));
        await ReadAsync(payload, source);

        _clock.Advance(Ttl * 0.5);
        (await refresher.RefreshNextAsync()).Should().BeNull();

        _clock.Advance(Ttl * 0.3);
        (await refresher.RefreshNextAsync()).Should().Be(payload.Key);

        source.Passes.Should().Be(2);
        (await ReadAsync(payload, source)).Version.Should().Be(2);
        payload.Peek(_cache)!.ComputedAtUtc.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task RefreshNext_StopsRefreshing_APayloadUnreadForTheIdleWindow()
    {
        var source = new Source();
        var payload = NewPayload();
        using var refresher = NewRefresher((payload, source));
        await ReadAsync(payload, source);

        // Keeps refreshing while the last read is recent — and its own passes are not reads.
        var refreshes = 0;
        var elapsed = TimeSpan.Zero;
        while (elapsed < CockpitCacheRefresher.IdleWindow)
        {
            _clock.Advance(Ttl);
            elapsed += Ttl;
            if (await refresher.RefreshNextAsync() is not null)
            {
                refreshes++;
            }
        }

        _clock.Advance(Ttl);
        (await refresher.RefreshNextAsync()).Should().BeNull();
        refreshes.Should().BeGreaterThan(0);

        // The next read is served the last value at once and wakes the refresh again.
        var stale = await ReadAsync(payload, source);
        stale.Version.Should().Be(refreshes + 1);
        (await refresher.RefreshNextAsync()).Should().Be(payload.Key);
    }

    [Fact]
    public async Task RefreshNext_RefreshesOnePayloadPerTick_MostOverdueFirst()
    {
        var fastSource = new Source();
        var slowSource = new Source();
        var fast = NewPayload(TimeSpan.FromSeconds(30));
        var slow = NewPayload(TimeSpan.FromMinutes(5));
        using var refresher = NewRefresher((fast, fastSource), (slow, slowSource));
        await ReadAsync(fast, fastSource);
        await ReadAsync(slow, slowSource);

        _clock.Advance(TimeSpan.FromMinutes(5));
        (await refresher.RefreshNextAsync()).Should().Be(fast.Key);
        (await refresher.RefreshNextAsync()).Should().Be(slow.Key);
        (await refresher.RefreshNextAsync()).Should().BeNull();

        fastSource.Passes.Should().Be(2);
        slowSource.Passes.Should().Be(2);
    }

    [Fact]
    public async Task RefreshNext_KeepsTheLastGoodValue_WhenARefreshFails_AndBacksOff()
    {
        var source = new Source();
        var payload = NewPayload();
        using var refresher = NewRefresher((payload, source));
        var good = await ReadAsync(payload, source);
        var computedAt = payload.Peek(_cache)!.ComputedAtUtc;

        source.Fails = true;
        _clock.Advance(Ttl);
        (await refresher.RefreshNextAsync()).Should().Be(payload.Key);

        (await ReadAsync(payload, source)).Should().BeSameAs(good);
        payload.Peek(_cache)!.ComputedAtUtc.Should().Be(computedAt);

        // Inside the failure's backoff window the refresher does not try again.
        (await refresher.RefreshNextAsync()).Should().BeNull();
        source.Passes.Should().Be(2);
    }

    [Fact]
    public async Task RefreshAsync_JoinsAColdRead_InsteadOfComputingTwice()
    {
        var source = new Source(gated: true);
        var payload = NewPayload();

        var read = ReadAsync(payload, source);
        var refresh = payload.RefreshAsync(_cache, source.ComputeAsync, _clock);
        source.Release();

        (await refresh).Should().BeSameAs(await read);
        source.Passes.Should().Be(1);
    }

    private WarmPayload<Value> NewPayload(TimeSpan? ttl = null)
        => new($"test:{Guid.NewGuid():N}", ttl ?? Ttl, TimeSpan.FromSeconds(30));

    private Task<Value> ReadAsync(WarmPayload<Value> payload, Source source)
        => payload.GetAsync(_cache, source.ComputeAsync, _clock, CancellationToken.None, ownerAwaitsToCompletion: true);

    private CockpitCacheRefresher NewRefresher(params (WarmPayload<Value> Payload, Source Source)[] payloads)
    {
        var targets = payloads.Select(entry => new WarmPayloadTarget(
            entry.Payload,
            _ => entry.Payload.RefreshAsync(_cache, entry.Source.ComputeAsync, _clock)));
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new CockpitCacheRefresher(targets, _cache, scopes, _clock, NullLogger<CockpitCacheRefresher>.Instance);
    }

    private sealed record Value(int Version);

    private sealed class Source(bool gated = false)
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _passes;

        public int Passes => Volatile.Read(ref _passes);

        public bool Fails { get; set; }

        public void Release() => _gate.TrySetResult();

        public async Task<Value> ComputeAsync()
        {
            var version = Interlocked.Increment(ref _passes);
            if (gated)
            {
                await _gate.Task;
            }

            return Fails
                ? throw new InvalidOperationException("57014: canceling statement due to statement timeout")
                : new Value(version);
        }
    }
}
