using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Data.Logging;
using Data.Metrics.Mongo;
using Ingestor.Options;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using TrueMain.Services.Ops.Stats;

namespace TrueMain.UnitTests;

/// <summary>
/// #1636: the <c>TrueMain.Ingestor</c> meter was declared with nothing collecting it. Every
/// instrument it publishes must reach the rollup store, folded per minute and tag set, and
/// the read side must group them back per instrument.
/// </summary>
public sealed class IngestorMeterExporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task FlushAsync_WritesEveryIngestorInstrument_FoldedPerMinuteAndTagSet()
    {
        await using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        var store = new RecordingStore();
        using var exporter = new IngestorMeterExporter(
            meterFactory, store, new FakeTimeProvider(Now), NullLogger<IngestorMeterExporter>.Instance);
        var metrics = new IngestorMetrics(meterFactory);

        metrics.RecordRunFailure("Discovery", JobMode.DiscoveryOnly);
        metrics.RecordRunFailure("Discovery", JobMode.DiscoveryOnly);
        metrics.RecordRiotRateLimitWait("europe", "match-v5.getMatch", TimeSpan.FromMilliseconds(120));
        metrics.RecordRiotRateLimitWait("europe", "match-v5.getMatch", TimeSpan.FromMilliseconds(80));
        metrics.RecordRiotRateLimitRejection("euw1", "league-v4.entries", "method");

        await exporter.FlushAsync(CancellationToken.None);

        store.Written.Should().HaveCount(3);
        store.Written.Should().AllSatisfy(rollup =>
        {
            rollup.Meter.Should().Be(IngestorMetrics.MeterName);
            rollup.BucketStartUtc.Should().Be(new DateTime(2026, 10, 6, 12, 34, 0, DateTimeKind.Utc));
        });

        var failures = store.Single(IngestorMetrics.RunFailuresCounterName);
        failures.Kind.Should().Be(MeterRollup.CounterKind);
        failures.Count.Should().Be(2);
        failures.Sum.Should().Be(2);
        failures.Tags.Should().Contain("process", "Discovery").And.Contain("mode", "DiscoveryOnly");

        var wait = store.Single(IngestorMetrics.RiotRateLimitWaitHistogramName);
        wait.Kind.Should().Be(MeterRollup.HistogramKind);
        wait.Unit.Should().Be("ms");
        wait.Count.Should().Be(2);
        wait.Sum.Should().Be(200);
        wait.Max.Should().Be(120);

        var rejections = store.Single(IngestorMetrics.RiotRateLimitRejectionsCounterName);
        rejections.Tags.Should().Contain("limit_type", "method");
    }

    [Fact]
    public async Task FlushAsync_AfterADrain_WritesNothingNew()
    {
        await using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        var store = new RecordingStore();
        using var exporter = new IngestorMeterExporter(
            meterFactory, store, new FakeTimeProvider(Now), NullLogger<IngestorMeterExporter>.Instance);
        new IngestorMetrics(meterFactory).RecordRunFailure("Harvest", JobMode.HarvestOnly);

        await exporter.FlushAsync(CancellationToken.None);
        await exporter.FlushAsync(CancellationToken.None);

        store.Calls.Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_IgnoresTheSameMeterFromAnotherFactory()
    {
        await using var own = new ServiceCollection().AddMetrics().BuildServiceProvider();
        await using var other = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var store = new RecordingStore();
        using var exporter = new IngestorMeterExporter(
            own.GetRequiredService<IMeterFactory>(), store, new FakeTimeProvider(Now),
            NullLogger<IngestorMeterExporter>.Instance);

        new IngestorMetrics(other.GetRequiredService<IMeterFactory>()).RecordRunFailure("Harvest", JobMode.HarvestOnly);
        await exporter.FlushAsync(CancellationToken.None);

        store.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task FlushAsync_WhenTheStoreThrows_DropsTheBatchWithoutThrowing()
    {
        await using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        var store = new RecordingStore { Throw = true };
        var logger = new FakeLogger<IngestorMeterExporter>();
        using var exporter = new IngestorMeterExporter(meterFactory, store, new FakeTimeProvider(Now), logger);
        new IngestorMetrics(meterFactory).RecordRunFailure("Harvest", JobMode.HarvestOnly);

        await exporter.FlushAsync(CancellationToken.None);

        logger.Collector.LatestRecord.Level.Should().Be(LogLevel.Warning);
        store.Throw = false;
        await exporter.FlushAsync(CancellationToken.None);
        store.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_WithAHungStore_GivesUpOnTheFinalFlushAfterItsTimeout()
    {
        await using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        var time = new FakeTimeProvider(Now);
        var store = new RecordingStore { Hang = true };
        var logger = new FakeLogger<IngestorMeterExporter>();
        using var exporter = new IngestorMeterExporter(meterFactory, store, time, logger);

        await exporter.StartAsync(CancellationToken.None);
        new IngestorMetrics(meterFactory).RecordRunFailure("Harvest", JobMode.HarvestOnly);
        var stopping = exporter.StopAsync(CancellationToken.None);

        // Wait (in real time) for the final flush to reach the store, then let its budget lapse.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (store.Calls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        store.Calls.Should().Be(1);
        time.Advance(IngestorMeterExporter.FinalFlushTimeout);
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        logger.Collector.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.Collector.LatestRecord.Message.Should().Contain("cancelled");
    }

    [Fact]
    public void Accumulator_SplitsMinutes_AndKeysTagsRegardlessOfOrder()
    {
        var time = new FakeTimeProvider(Now);
        var accumulator = new MeterRollupAccumulator(time);
        using var meter = new Meter("test.accumulator");
        var counter = meter.CreateCounter<long>("test.counter");

        accumulator.Record(counter, 1, [new("a", "1"), new("b", "2")]);
        accumulator.Record(counter, 1, [new("b", "2"), new("a", "1")]);
        time.Advance(TimeSpan.FromMinutes(1));
        accumulator.Record(counter, 1, [new("a", "1"), new("b", "2")]);

        var rollups = accumulator.Drain();

        rollups.Should().HaveCount(2);
        rollups.Select(rollup => rollup.Count).Should().BeEquivalentTo([2L, 1L]);
        MeterRollup.SeriesKeyOf(rollups[0].Tags).Should().Be("a=1|b=2");
        accumulator.Drain().Should().BeEmpty();
    }

    [Fact]
    public void BuildInstruments_GroupsSeriesPerInstrument_WithTotalsAndMeans()
    {
        var at = Now.UtcDateTime;
        MeterSeriesTotal[] series =
        [
            new("ingestor.riot.ratelimit.wait", MeterRollup.HistogramKind, "ms", "wait", Tags("routing_value", "asia"), 2, 50, 30, at),
            new("ingestor.riot.ratelimit.wait", MeterRollup.HistogramKind, "ms", "wait", Tags("routing_value", "europe"), 4, 400, 250, at),
            new("ingestor.run.failures", MeterRollup.CounterKind, "{failure}", "failures", Tags("process", "Discovery"), 3, 3, 1, at)
        ];

        var instruments = IngestorMetricsQueryService.BuildInstruments(series);

        instruments.Select(instrument => instrument.Name)
            .Should().Equal("ingestor.riot.ratelimit.wait", "ingestor.run.failures");
        var wait = instruments[0];
        wait.Count.Should().Be(6);
        wait.Sum.Should().Be(450);
        wait.Max.Should().Be(250);
        wait.Series[0].Tags.Should().Contain("routing_value", "europe");
        wait.Series[0].Mean.Should().Be(100);
    }

    [Fact]
    public void SourceGeneratedOpsEvent_KeepsTheIdAndNameTheMongoSinkResolves()
    {
        var logger = new FakeLogger();

        logger.CandidateValidated(2, "EUW1", "puuid");
        logger.MatchRevertFailed(new InvalidOperationException("boom"), "EUW1", "puuid");

        var records = logger.Collector.GetSnapshot();
        OpsEvents.Resolve(records[0].Id).Should().Be(nameof(OpsEvents.CandidateValidated));
        OpsEvents.Resolve(records[1].Id).Should().Be(nameof(OpsEvents.MatchRevertFailed));
        records[0].Message.Should().Be("Validated 2 candidates for EUW1/puuid.");
    }

    private static Dictionary<string, string> Tags(string key, string value) => new() { [key] = value };

    private sealed class RecordingStore : IMeterRollupStore
    {
        public List<MeterRollup> Written { get; } = [];

        public int Calls { get; private set; }

        public bool Throw { get; set; }

        public bool Hang { get; set; }

        public MeterRollup Single(string instrument) => Written.Single(rollup => rollup.Instrument == instrument);

        public Task<int> WriteAsync(IReadOnlyCollection<MeterRollup> rollups, CancellationToken ct)
        {
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("Mongo is down");
            }

            if (Hang)
            {
                return HangAsync(ct);
            }

            Written.AddRange(rollups);
            return Task.FromResult(rollups.Count);
        }

        private static async Task<int> HangAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public Task<MeterRollupWindow> GetWindowAsync(string meter, DateTime sinceUtc, CancellationToken ct)
            => Task.FromResult(new MeterRollupWindow(sinceUtc, [], null));
    }
}
