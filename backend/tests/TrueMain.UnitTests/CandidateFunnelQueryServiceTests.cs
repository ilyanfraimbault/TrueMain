using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using NSubstitute;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The funnel's two hard parts: attributing six processes' counters to the right
/// series, and the forward-only validated counter — which must read as absent, never
/// as zero, for periods that predate it (#924, #1024).
/// </summary>
public sealed class CandidateFunnelQueryServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_AttributesEachProcessToItsOwnSeries()
    {
        var yesterday = Now.AddDays(-1);
        var store = StoreWith(
            DiscoveryRun(yesterday.AddHours(1), ("EUW1", 30), ("KR", 12)),
            HarvestRun(yesterday.AddHours(2), inserted: 7),
            ManualSeedRun(yesterday.AddHours(3), queued: 2),
            ScoringRun(yesterday.AddHours(4), ("EUW1", 100, 20), ("KR", 40, 10)),
            MatchIngestionRun(yesterday.AddHours(5), validated: 25),
            MainAnalysisRun(yesterday.AddHours(6), demoted: 3));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        var day = result.Buckets.Single(bucket => bucket.Bucket == "2026-08-04T00:00:00Z");
        day.IntakeLadder.Should().Be(42, "discovery reports per platform and has no run-level total");
        day.IntakeHarvest.Should().Be(7);
        day.IntakeManual.Should().Be(2);
        day.Scored.Should().Be(140);
        day.Promoted.Should().Be(30);
        day.Validated.Should().Be(25);
        day.Demoted.Should().Be(3);
        day.Runs.Should().Be(6);
    }

    [Fact]
    public async Task GetAsync_ReportsValidatedAsAbsentBeforeTheCounterExisted()
    {
        // Two ingestion runs: the older one predates the counter and has no such key,
        // the newer one carries it. The old bucket may not read as a validated zero —
        // the pipeline was validating accounts then, nothing was counting them.
        var store = StoreWith(
            new ProcessRunSummarySample(
                "MatchIngestion",
                Now.AddDays(-3),
                """{"accountsProcessed":9,"matchesInserted":30,"matchesSkipped":0,"timelinesUpdated":0,"errors":0,"byPlatform":[]}"""),
            MatchIngestionRun(Now.AddDays(-1), validated: 4));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        result.ValidatedFirstMeasuredAtUtc.Should().Be(Now.AddDays(-1));
        result.Buckets.Single(bucket => bucket.Bucket == "2026-08-02T00:00:00Z")
            .Validated.Should().BeNull("that run recorded no such counter");
        result.Buckets.Single(bucket => bucket.Bucket == "2026-08-04T00:00:00Z")
            .Validated.Should().Be(4);
    }

    [Fact]
    public async Task GetAsync_ReportsAZeroValidatedCountOnceTheCounterExists()
    {
        // A quiet period *after* the counter shipped genuinely validated nothing, and
        // saying so is the point of the panel — absent-vs-zero cuts both ways.
        var store = StoreWith(
            MatchIngestionRun(Now.AddDays(-3), validated: 5),
            MatchIngestionRun(Now.AddDays(-1), validated: 0));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        result.Buckets.Single(bucket => bucket.Bucket == "2026-08-04T00:00:00Z")
            .Validated.Should().Be(0);
        result.Buckets.Single(bucket => bucket.Bucket == "2026-08-03T00:00:00Z")
            .Validated.Should().Be(0, "a day inside the measured range with no run still measured zero");
    }

    [Fact]
    public async Task GetAsync_LeavesValidatedNullWhenNoRunEverMeasuredIt()
    {
        var store = StoreWith(HarvestRun(Now.AddDays(-1), inserted: 3));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        result.ValidatedFirstMeasuredAtUtc.Should().BeNull();
        result.Buckets.Should().OnlyContain(bucket => bucket.Validated == null);
    }

    [Fact]
    public async Task GetAsync_CountsRunsThatRecordedNoUsableSummaryAsAttempts()
    {
        // A failed run has no summary; a no-work pass records one of a different shape.
        // Neither moved a candidate, and neither is a period without an attempt.
        var store = StoreWith(
            new ProcessRunSummarySample("Discovery", Now.AddHours(-4), null),
            new ProcessRunSummarySample("Scoring", Now.AddHours(-3), "not json at all"),
            new ProcessRunSummarySample("Harvest", Now.AddHours(-2), """{"reason":"No platforms configured.","candidatesInserted":0}"""));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        var today = result.Buckets.Single(bucket => bucket.Bucket == "2026-08-05T00:00:00Z");
        today.Runs.Should().Be(3, "three passes genuinely happened");
        today.IntakeLadder.Should().Be(0);
        today.Scored.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_FillsQuietPeriodsInsideTheObservedRangeWithZeros()
    {
        var store = StoreWith(
            HarvestRun(Now.AddDays(-4), inserted: 5),
            HarvestRun(Now, inserted: 2));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 30, CancellationToken.None);

        result.Buckets.Select(bucket => bucket.Runs).Should().Equal([1, 0, 0, 0, 1]);
    }

    [Fact]
    public async Task GetAsync_DoesNotZeroFillBeforeTheOldestRunItCanSee()
    {
        // Anything older was not measured — retention took it — and filling it with
        // zeros would assert an idle pipeline there is no record of.
        var store = StoreWith(HarvestRun(Now.AddDays(-1), inserted: 1));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 90, CancellationToken.None);

        result.Buckets.Should().HaveCount(2);
        result.EarliestRunAtUtc.Should().Be(Now.AddDays(-1));
        result.RetentionDays.Should().Be(180);
        result.WindowDays.Should().Be(90);
    }

    [Fact]
    public async Task GetAsync_StartsWeeksOnMonday()
    {
        // 2026-08-05 is a Wednesday; its week starts on Monday the 3rd.
        var store = StoreWith(HarvestRun(Now, inserted: 4));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Week, windowDays: 7, CancellationToken.None);

        result.Buckets.Should().ContainSingle()
            .Which.Bucket.Should().Be("2026-08-03T00:00:00Z");
    }

    [Fact]
    public async Task GetAsync_GroupsByCalendarMonth()
    {
        var store = StoreWith(
            HarvestRun(new DateTime(2026, 7, 30, 8, 0, 0, DateTimeKind.Utc), inserted: 3),
            HarvestRun(new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc), inserted: 4));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Month, windowDays: 60, CancellationToken.None);

        result.Buckets.Select(bucket => bucket.Bucket)
            .Should().Equal(["2026-07-01T00:00:00Z", "2026-08-01T00:00:00Z"]);
        result.Buckets[0].IntakeHarvest.Should().Be(3);
        result.Buckets[1].IntakeHarvest.Should().Be(4);
    }

    [Fact]
    public async Task GetAsync_ReturnsNoBucketsWhenNoRunSurvives()
    {
        var result = await CreateService(StoreWith()).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 30, CancellationToken.None);

        result.Buckets.Should().BeEmpty("an empty range is not a range of zeros");
        result.EarliestRunAtUtc.Should().BeNull();
        result.ValidatedFirstMeasuredAtUtc.Should().BeNull();
    }

    /// <summary>
    /// The store stub, serving the samples oldest-first the way the real one does — the
    /// service relies on that order both to find the earliest run it may zero-fill from
    /// and to date the first run that measured the validated counter.
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

    private static CandidateFunnelQueryService CreateService(IProcessRunStore store)
        => new(
            store,
            Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
            {
                ProcessRunsRetention = TimeSpan.FromDays(180)
            }),
            new FixedTimeProvider(Now));

    // The fake summaries below are shaped exactly like their records serialize
    // (camelCase, locked by ProcessRunSummaryJsonTests) — the parsing under test reads
    // those keys, so a rename that broke production would break these too.

    private static ProcessRunSummarySample DiscoveryRun(
        DateTime startedAtUtc,
        params (string Platform, int CandidatesInserted)[] platforms)
    {
        var entries = platforms.Select(platform =>
            $$"""
              {"platform":"{{platform.Platform}}","accountsProcessed":40,"newAccounts":3,"candidatesInserted":{{platform.CandidatesInserted}},"candidatesUpdated":5,"rankSnapshotsInserted":2,"rankSnapshotsUpdated":6,"rankSnapshotsUnchanged":32,"error":null}
              """);
        return new ProcessRunSummarySample(
            "Discovery",
            startedAtUtc,
            $$"""{"platforms":[{{string.Join(",", entries)}}]}""");
    }

    private static ProcessRunSummarySample ScoringRun(
        DateTime startedAtUtc,
        params (string Platform, int Scored, int Queued)[] platforms)
    {
        var entries = platforms.Select(platform =>
            $$"""{"platform":"{{platform.Platform}}","scored":{{platform.Scored}},"queued":{{platform.Queued}}}""");
        return new ProcessRunSummarySample(
            "Scoring",
            startedAtUtc,
            $$"""{"platforms":[{{string.Join(",", entries)}}]}""");
    }

    private static ProcessRunSummarySample HarvestRun(DateTime startedAtUtc, int inserted)
        => new(
            "Harvest",
            startedAtUtc,
            $$"""{"candidatesInserted":{{inserted}},"candidatesUpdated":0,"accountsCreated":0,"eligibleNew":0,"selectedNew":0,"eligibleKnown":0,"selectedKnown":0,"budgetExhausted":false}""");

    private static ProcessRunSummarySample ManualSeedRun(DateTime startedAtUtc, int queued)
        => new(
            "ManualSeed",
            startedAtUtc,
            $$"""{"claimed":2,"ingested":2,"notFound":0,"failed":0,"candidatesQueued":{{queued}}}""");

    private static ProcessRunSummarySample MatchIngestionRun(DateTime startedAtUtc, int validated)
        => new(
            "MatchIngestion",
            startedAtUtc,
            $$"""{"accountsProcessed":9,"matchesInserted":30,"matchesSkipped":4,"timelinesUpdated":12,"errors":0,"accountsValidated":{{validated}},"byPlatform":[]}""");

    private static ProcessRunSummarySample MainAnalysisRun(DateTime startedAtUtc, int demoted)
        => new(
            "MainAnalysis",
            startedAtUtc,
            $$"""{"accountsProcessed":50,"statsUpserted":40,"statsRemoved":2,"demotedAccounts":{{demoted}}}""");

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
