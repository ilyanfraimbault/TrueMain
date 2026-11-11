using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TrueMain.Services;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Warmup;

namespace TrueMain.UnitTests;

public sealed class ChampionWarmupTests
{
    private const string Key = "champions:scaling:103:MIDDLE:16.4:ALL";

    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly ChampionWarmupOptions Defaults = new();

    [Fact]
    public async Task AReaderKeepsThePublishedVersionWhileAPassComputesTheNextOne()
    {
        using var cache = SizedCache();
        var version = NewVersion();
        var subject = new ChampionReadCache(new FailingStamp(), cache, version);
        version.Publish("v1");

        (await subject.GetOrComputeAsync(Key, _ => Task.FromResult("old"), default)).Should().Be("old");

        await RunInPassAsync(version, "v2", async () =>
            (await subject.GetOrComputeAsync(Key, _ => Task.FromResult("new"), default)).Should().Be("new"));

        // The pass has finished computing but has not published: a reader must not see it,
        // and must not compute anything either.
        (await subject.GetOrComputeAsync<string>(Key, _ => throw new InvalidOperationException("recomputed"), default))
            .Should().Be("old");
    }

    [Fact]
    public async Task PublishingMovesEveryReaderToWhatThePassComputedWithoutRecomputing()
    {
        using var cache = SizedCache();
        var version = NewVersion();
        var subject = new ChampionReadCache(new FailingStamp(), cache, version);
        await RunInPassAsync(version, "v2", () => subject.GetOrComputeAsync(Key, _ => Task.FromResult("new"), default));

        version.Publish("v2");

        (await subject.GetOrComputeAsync<string>(Key, _ => throw new InvalidOperationException("recomputed"), default))
            .Should().Be("new");
    }

    [Fact]
    public async Task AReadWithABackoffIsNotRunAgainAfterAFailureWhateverTheVersion()
    {
        using var cache = SizedCache();
        var version = NewVersion();
        var subject = new ChampionReadCache(new FailingStamp(), cache, version);
        var runs = 0;
        Task<string> Failing(CancellationToken _)
        {
            runs++;
            throw new InvalidOperationException("statement timeout");
        }

        version.Publish("v1");
        var first = () => subject.GetOrComputeAsync(Key, Failing, default, failureBackoff: TimeSpan.FromMinutes(1));
        await first.Should().ThrowAsync<InvalidOperationException>();

        version.Publish("v2");
        var second = () => subject.GetOrComputeAsync(Key, Failing, default, failureBackoff: TimeSpan.FromMinutes(1));
        await second.Should().ThrowAsync<ReadBackoffException>();

        runs.Should().Be(1);
    }

    [Fact]
    public async Task APinnedReaderNeverAsksForTheStamp()
    {
        using var cache = SizedCache();
        var version = NewVersion();
        version.Publish("v1");

        // FailingStamp throws if the read comes anywhere near the max() it replaces.
        var read = () => new ChampionReadCache(new FailingStamp(), cache, version)
            .GetOrComputeAsync(Key, _ => Task.FromResult("value"), default);

        await read.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ARequestCarryingAWrongKeyStaysAnOrdinaryRequest()
    {
        var version = NewVersion();

        await Task.Run(() =>
        {
            version.TryEnterPass("not-the-key:v2").Should().BeFalse();
            version.TryEnterPass(null).Should().BeFalse();
            version.IsWarming.Should().BeFalse();
        });
    }

    [Fact]
    public async Task ARequestCarryingTheKeyEntersThePassItNames()
    {
        var version = NewVersion();

        await Task.Run(() =>
        {
            version.TryEnterPass($"{version.RequestKey}:v2").Should().BeTrue();
            version.Current.Should().Be("v2");
            version.IsWarming.Should().BeTrue();
        });

        // Ambient to that flow only: the next request on the same connection is a visitor.
        version.IsWarming.Should().BeFalse();
    }

    [Fact]
    public void AnEntryComputedByAPassOutlivesTheWaitForTheNextOne()
    {
        var version = NewVersion();
        var backstop = TimeSpan.FromMinutes(30);

        version.EntryLifetime(backstop).Should().Be(backstop);

        version.EnterPass("v2");

        version.EntryLifetime(backstop).Should().BeGreaterThan(TimeSpan.FromMinutes(Defaults.MaxPassAgeMinutes));
    }

    [Theory]
    [InlineData("s1", null, null, true)] // never ran
    [InlineData("s2", "s1", 600, true)] // a new stamp
    [InlineData("s2", "s1", 299, false)] // a new stamp, inside the minimum interval
    [InlineData("s1", "s1", 600, false)] // nothing new
    [InlineData("s1", "s1", 5400, true)] // nothing new, but the last pass has reached the age limit
    public void APassIsDueOnANewStampOrAtTheAgeLimit(string stamp, string? lastStamp, int? secondsAgo, bool due)
    {
        var options = new ChampionWarmupOptions { MinPassIntervalSeconds = 300, MaxPassAgeMinutes = 90 };
        DateTimeOffset? lastPassEndedAt = secondsAgo is { } seconds ? Noon.AddSeconds(-seconds) : null;

        ChampionCacheWarmer.IsPassDue(stamp, lastStamp, lastPassEndedAt, Noon, options).Should().Be(due);
    }

    [Theory]
    [InlineData(0, 1000, true)]
    [InlineData(100, 1000, true)]
    [InlineData(101, 1000, false)] // Postgres went away part of the way through
    [InlineData(1000, 1000, false)]
    public void APassThatMostlyFailedIsNotPublished(int failures, int total, bool published)
        => ChampionCacheWarmer.IsWorthPublishing(failures, total).Should().Be(published);

    [Fact]
    public async Task ReadinessHoldsUntilTheFirstPassThenOpens()
    {
        var clock = new FakeTimeProvider(Noon);
        var version = new ChampionReadVersion(clock, Microsoft.Extensions.Options.Options.Create(Defaults));
        var subject = new ChampionWarmupHealthCheck(
            version, Microsoft.Extensions.Options.Options.Create(Defaults), clock, NullLogger<ChampionWarmupHealthCheck>.Instance);

        (await subject.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Degraded);

        version.Publish("v1");

        (await subject.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AStuckFirstPassCannotKeepTheSiteDownPastTheBudget()
    {
        var clock = new FakeTimeProvider(Noon);
        var version = new ChampionReadVersion(clock, Microsoft.Extensions.Options.Options.Create(Defaults));
        var subject = new ChampionWarmupHealthCheck(
            version, Microsoft.Extensions.Options.Options.Create(Defaults), clock, NullLogger<ChampionWarmupHealthCheck>.Instance);

        clock.Advance(TimeSpan.FromSeconds(Defaults.ReadyBudgetSeconds));

        (await subject.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void TheCatalogCoversEveryRankTheSiteOffersOnEachPatchAndPopulation()
    {
        var urls = ChampionWarmCatalog.Directory(["16.21", "16.20"]);

        // All ranks, nine "and above" (Challenger has none) and ten single tiers.
        ChampionWarmCatalog.RankFilters.Should().HaveCount(20);
        ChampionWarmCatalog.RankFilters.Should().NotContain("CHALLENGER_PLUS");

        urls.Should().Contain("/champions/directory?pageSize=50&patch=16.20&eloBracket=GOLD_PLUS");
        urls.Should().Contain("/champions/directory?pageSize=50&patch=16.21&eloBracket=GOLD&truemainsOnly=false");
        urls.Should().Contain("/champions/directory?pageSize=50&eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/directory?pageSize=50&position=TOP&eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/tierlist?eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/tierlist?position=TOP&eloBracket=ALL&truemainsOnly=false");
        urls.Should().Contain("/champions/overview");
        urls.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheCatalogAsksForAChampionPageTheWayThePageDoes()
    {
        var urls = ChampionWarmCatalog.ChampionPages(
            [new ChampionWarmLine(99, "UTILITY"), new ChampionWarmLine(99, "MIDDLE")], "16.21", "MASTER_PLUS");

        urls.Should().Contain("/champions/99?eloBracket=MASTER_PLUS&truemainsOnly=true");
        urls.Should().Contain("/champions/99/trend?eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/99?position=UTILITY&patch=16.21&eloBracket=MASTER_PLUS&truemainsOnly=true");
        urls.Should().Contain("/champions/99/trend?position=MIDDLE&eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/directory?pageSize=1&championId=99&position=UTILITY&patch=16.21&eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/99/scaling?position=UTILITY&patch=16.21&eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/99/item-context?position=UTILITY&patch=16.21");
        urls.Should().Contain("/champions/99/matchups?position=UTILITY&patch=16.21&eloBracket=MASTER_PLUS");
        urls.Should().Contain("/champions/99/synergies?position=UTILITY&eloBracket=MASTER_PLUS&patch=16.21");
        urls.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheCatalogFillsEveryLeaderboardSortAndOneClickFilter()
    {
        var urls = ChampionWarmCatalog.Leaderboard();

        urls.Should().Contain("/truemains?page=1&pageSize=25");
        urls.Should().Contain("/truemains?page=1&pageSize=25&sort=dedication");
        urls.Should().Contain("/truemains?page=1&pageSize=25&sort=winRate");
        urls.Should().Contain("/truemains?page=1&pageSize=25&position=MIDDLE");
        urls.Should().Contain("/truemains?page=1&pageSize=25&region=korea");
        urls.Should().OnlyHaveUniqueItems();
    }

    private static ChampionReadVersion NewVersion()
        => new(TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new ChampionWarmupOptions()));

    private static MemoryCache SizedCache() => new(new MemoryCacheOptions { SizeLimit = 1024 });

    // In a flow of its own: a pass is ambient to its async flow, and the assertions around it
    // play the visitor.
    private static async Task RunInPassAsync(ChampionReadVersion version, string pass, Func<Task> body)
        => await Task.Run(async () =>
        {
            version.EnterPass(pass);
            await body();
        });

    private sealed class FailingStamp : IChampionAggregationStamp
    {
        public Task<DateTime?> GetLatestAsync(CancellationToken ct)
            => throw new InvalidOperationException("A pinned read must not ask for the stamp.");
    }
}
