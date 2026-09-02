using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

/// <summary>
/// #1368: champion reads are cached until the ingestor publishes new aggregates, not
/// for a fixed 60 seconds, and concurrent callers that miss share one pass. Both halves
/// are the kind of thing that "obviously works" and then silently does not — an entry
/// with no <c>Size</c> is dropped by a size-limited cache without a word, and a version
/// that is not part of the key serves yesterday's numbers for ever — so both are pinned
/// here against a real <see cref="MemoryCache"/> configured the way the API's is.
/// </summary>
public sealed class ChampionReadCacheTests
{
    private const string Key = "champions:roam:103:MIDDLE:16.4:ALL";

    private static readonly DateTime FirstCycle = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondCycle = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_second_caller_is_served_from_the_cache_without_recomputing()
    {
        using var cache = SizedCache();
        var reads = new CountingStamp(FirstCycle);
        var subject = new ChampionReadCache(reads, cache);
        var computations = 0;

        var first = await subject.GetOrComputeAsync(Key, _ => Count(ref computations), default);
        var second = await subject.GetOrComputeAsync(Key, _ => Count(ref computations), default);

        first.Should().Be(1);
        second.Should().Be(1);
        computations.Should().Be(1);
    }

    [Fact]
    public async Task The_entry_is_sized_so_a_size_limited_cache_actually_keeps_it()
    {
        // Asserting the value is really in the cache — rather than only that a second
        // call returns the same number — is what distinguishes a stored entry from one
        // MemoryCache dropped on the floor for having no Size.
        using var cache = SizedCache();
        var subject = new ChampionReadCache(new CountingStamp(FirstCycle), cache);
        var computations = 0;

        await subject.GetOrComputeAsync(Key, _ => Count(ref computations), default);

        cache.TryGetValue(VersionedKey(FirstCycle), out var stored).Should().BeTrue();
        stored.Should().Be(1);
    }

    [Fact]
    public async Task The_version_token_is_read_once_per_burst_not_once_per_read()
    {
        // The token read is the one query every champion request now makes, so it must
        // not itself become the hot query: it is cached and single-flighted like the
        // reads it keys.
        using var cache = SizedCache();
        var stamp = new CountingStamp(FirstCycle);
        var subject = new ChampionReadCache(stamp, cache);
        var computations = 0;

        for (var i = 0; i < 20; i++)
        {
            await subject.GetOrComputeAsync($"{Key}:{i}", _ => Count(ref computations), default);
        }

        stamp.Reads.Should().Be(1);
    }

    [Fact]
    public async Task A_new_aggregation_cycle_retires_every_entry_without_evicting_anything()
    {
        using var cache = SizedCache();
        var stamp = new CountingStamp(FirstCycle);
        var subject = new ChampionReadCache(stamp, cache);
        var computations = 0;

        (await subject.GetOrComputeAsync(Key, _ => Count(ref computations), default)).Should().Be(1);

        // The token is cached for VersionTtl; drop it the way its expiry would, since
        // what is under test is the key, not the five-second window.
        cache.Remove(ChampionReadCache.VersionCacheKey);
        stamp.Latest = SecondCycle;

        (await subject.GetOrComputeAsync(Key, _ => Count(ref computations), default))
            .Should().Be(2, "the aggregation stamp moved, so the cached answer is stale");

        // Both versions coexist under their own key — nothing had to be enumerated or
        // evicted for the new numbers to be served.
        cache.TryGetValue(VersionedKey(FirstCycle), out _).Should().BeTrue();
        cache.TryGetValue(VersionedKey(SecondCycle), out _).Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_aggregate_table_is_a_version_of_its_own()
    {
        using var cache = SizedCache();
        var subject = new ChampionReadCache(new CountingStamp(null), cache);
        var computations = 0;

        await subject.GetOrComputeAsync(Key, _ => Count(ref computations), default);

        // A never-aggregated database still caches, and the first cycle to land moves
        // the token off "none" — so the empty answer is never served again.
        cache.TryGetValue(
            ChampionReadCache.ComposeKey(ChampionReadCache.EmptyVersion, Key), out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_callers_that_miss_share_a_single_pass()
    {
        using var cache = SizedCache();
        var subject = new ChampionReadCache(new CountingStamp(FirstCycle), cache);

        var started = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> Slow(CancellationToken ct)
        {
            Interlocked.Increment(ref started);
            await release.Task;
            return 42;
        }

        var callers = Enumerable.Range(0, 8)
            .Select(_ => subject.GetOrComputeAsync("champions:synergies-trio:103", Slow, default))
            .ToList();

        release.SetResult();
        var results = await Task.WhenAll(callers);

        results.Should().AllSatisfy(result => result.Should().Be(42));
        started.Should().Be(1, "eight simultaneous misses are one scan, not eight");
    }

    [Fact]
    public async Task A_caller_that_walks_away_does_not_cancel_the_pass_for_the_others()
    {
        // The pass runs on the owner's request-scoped DbContext, so it must outlive a
        // cancelled caller rather than die with it and take every joiner down.
        using var cache = SizedCache();
        var subject = new ChampionReadCache(new CountingStamp(FirstCycle), cache);

        using var abandoned = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> Slow(CancellationToken ct)
        {
            await release.Task;
            ct.IsCancellationRequested.Should().BeFalse("the shared pass is not any one caller's");
            return 7;
        }

        var owner = subject.GetOrComputeAsync("champions:roam:cancel", Slow, abandoned.Token);
        await abandoned.CancelAsync();
        release.SetResult();

        (await owner).Should().Be(7);
    }

    private static Task<int> Count(ref int computations)
    {
        computations++;
        return Task.FromResult(computations);
    }

    private static string VersionedKey(DateTime cycle)
        => ChampionReadCache.ComposeKey(cycle.Ticks.ToString(CultureInfo.InvariantCulture), Key);

    // Same shape as the API's shared cache (Program.cs): the sizing rule only bites
    // when there is a limit.
    private static MemoryCache SizedCache() => new(new MemoryCacheOptions { SizeLimit = 1024 });

    private sealed class CountingStamp(DateTime? latest) : IChampionAggregationStamp
    {
        public DateTime? Latest { get; set; } = latest;

        public int Reads { get; private set; }

        public Task<DateTime?> GetLatestAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Latest);
        }
    }
}
