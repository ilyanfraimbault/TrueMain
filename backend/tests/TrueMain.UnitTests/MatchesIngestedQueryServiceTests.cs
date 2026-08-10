using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using NSubstitute;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The bucketing and the summary parsing, which is where this series can lie: the
/// counters live inside an opaque JSON string, and a run without one still has to
/// count as an attempt rather than vanish (a crash-looping ingestor must not read as
/// an idle one).
/// </summary>
public sealed class MatchesIngestedQueryServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_SumsTheCountersOfEveryRunInTheDay()
    {
        var store = StoreWith(
            Run(Now.AddDays(-1).AddHours(1), inserted: 30, skipped: 4, timelines: 12),
            Run(Now.AddDays(-1).AddHours(5), inserted: 12, skipped: 1, timelines: 6));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        var day = result.Buckets.Single(bucket => bucket.Bucket == "2026-08-04T00:00:00Z");
        day.MatchesInserted.Should().Be(42);
        day.MatchesSkipped.Should().Be(5);
        day.TimelinesUpdated.Should().Be(18);
        day.Runs.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_CountsRunsThatRecordedNoSummaryAsAttemptsWithNoCounters()
    {
        // A failed or abandoned run has no summary at all; a no-work pass records one
        // with a different shape. Neither carries matchesInserted, and neither is a
        // period without an attempt.
        var store = StoreWith(
            new ProcessRunSummarySample("MatchIngestion", Now.AddHours(-4), "not json at all"),
            new ProcessRunSummarySample("MatchIngestion", Now.AddHours(-3), """{"reason":"no accounts due","selected":0}"""),
            new ProcessRunSummarySample("MatchIngestion", Now.AddHours(-2), null));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        var today = result.Buckets.Single(bucket => bucket.Bucket == "2026-08-05T00:00:00Z");
        today.Runs.Should().Be(3, "three passes genuinely happened");
        today.MatchesInserted.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_FillsQuietPeriodsInsideTheObservedRangeWithZeros()
    {
        var store = StoreWith(
            Run(Now.AddDays(-4), inserted: 10),
            Run(Now, inserted: 5));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 30, CancellationToken.None);

        // Oldest run through today inclusive — the silent days between are the whole
        // point of the chart, so they are present at zero rather than missing.
        result.Buckets.Should().HaveCount(5);
        result.Buckets.Select(bucket => bucket.Runs).Should().Equal([1, 0, 0, 0, 1]);
        result.EarliestRunAtUtc.Should().Be(Now.AddDays(-4));
    }

    [Fact]
    public async Task GetAsync_DoesNotZeroFillBeforeTheOldestRunItCanSee()
    {
        var store = StoreWith(Run(Now.AddDays(-2), inserted: 7));

        // A 90-day window over two days of surviving history: the 88 days retention
        // already dropped were not measured, and claiming an idle pipeline for them
        // would be a fabrication.
        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 90, CancellationToken.None);

        result.Buckets.Should().HaveCount(3);
        result.Buckets[0].Bucket.Should().Be("2026-08-03T00:00:00Z");
        result.WindowDays.Should().Be(90);
        result.RetentionDays.Should().Be(180, "the panel states the bound it cannot see past");
    }

    [Fact]
    public async Task GetAsync_StartsWeeksOnMonday()
    {
        // 2026-08-05 is a Wednesday; its week starts Monday 2026-08-03, matching
        // Postgres' date_trunc('week') that the sibling chart uses.
        var store = StoreWith(Run(Now, inserted: 3));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Week, windowDays: 30, CancellationToken.None);

        result.Buckets.Should().ContainSingle();
        result.Buckets[0].Bucket.Should().Be("2026-08-03T00:00:00Z");
    }

    [Fact]
    public async Task GetAsync_GroupsByCalendarMonth()
    {
        var store = StoreWith(
            Run(new DateTime(2026, 7, 20, 3, 0, 0, DateTimeKind.Utc), inserted: 4),
            Run(new DateTime(2026, 8, 2, 3, 0, 0, DateTimeKind.Utc), inserted: 6));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Month, windowDays: 60, CancellationToken.None);

        result.Buckets.Select(bucket => bucket.Bucket)
            .Should().Equal(["2026-07-01T00:00:00Z", "2026-08-01T00:00:00Z"]);
        result.Buckets[0].MatchesInserted.Should().Be(4);
        result.Buckets[1].MatchesInserted.Should().Be(6);
    }

    [Fact]
    public async Task GetAsync_ReturnsNoBucketsWhenNoRunSurvives()
    {
        var result = await CreateService(StoreWith()).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 30, CancellationToken.None);

        result.Buckets.Should().BeEmpty("an empty range is not a range of zeros");
        result.EarliestRunAtUtc.Should().BeNull();
    }

    /// <summary>
    /// The store stub, serving the samples oldest-first the way the real one does —
    /// the service relies on that order to find the earliest run it may zero-fill
    /// from.
    /// </summary>
    private static IProcessRunStore StoreWith(params ProcessRunSummarySample[] samples)
    {
        var store = Substitute.For<IProcessRunStore>();
        store.GetRunSummariesAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ProcessRunSummarySample>>(
                _ => [.. samples.OrderBy(sample => sample.StartedAtUtc)]);
        return store;
    }

    private static MatchesIngestedQueryService CreateService(IProcessRunStore store)
        => new(
            store,
            Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
            {
                ProcessRunsRetention = TimeSpan.FromDays(180)
            }),
            new FixedTimeProvider(Now));

    // Shaped exactly like MatchIngestionSummary serializes (camelCase, locked by
    // ProcessRunSummaryJsonTests) — the parsing under test reads those keys.
    private static ProcessRunSummarySample Run(
        DateTime startedAtUtc,
        int inserted = 0,
        int skipped = 0,
        int timelines = 0)
        => new(
            "MatchIngestion",
            startedAtUtc,
            $$"""
              {"accountsProcessed":1,"matchesInserted":{{inserted}},"matchesSkipped":{{skipped}},"timelinesUpdated":{{timelines}},"errors":0,"accountsValidated":1,"byPlatform":[]}
              """);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
