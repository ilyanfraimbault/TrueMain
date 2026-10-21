using AwesomeAssertions;
using Data.Logging;
using Ingestor.FetchWorkers;
using Ingestor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace TrueMain.UnitTests;

/// <summary>
/// The brake in front of the match-fetch workers' claims (#1459): under a lowered threshold the
/// workers demonstrably stop claiming and resume once the resource recovers, with one ops event
/// for each transition — verified here rather than assumed.
/// </summary>
public sealed class IngestionBackpressureTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task WaitUntilClearAsync_BelowALoweredThreshold_HoldsTheClaimUntilTheResourceRecovers()
    {
        var probe = new ScriptedProbe(new HostResourceReading(60, 40));
        var time = new FakeTimeProvider();
        var logger = new FakeLogger<IngestionBackpressure>();
        var backpressure = Create(probe, time, logger, minFreeDiskPercent: 50);

        var waiting = backpressure.WaitUntilClearAsync(CancellationToken.None);

        waiting.IsCompleted.Should().BeFalse("40 % free is below the lowered 50 % threshold");
        backpressure.IsActive.Should().BeTrue();

        probe.Reading = new HostResourceReading(60, 70);
        await AdvanceUntilAsync(time, waiting);

        backpressure.IsActive.Should().BeFalse();
        Events(logger).Should().Equal(
            nameof(OpsEvents.IngestionBackpressureActivated),
            nameof(OpsEvents.IngestionBackpressureReleased));
        logger.Collector.GetSnapshot()
            .Single(record => record.Id == OpsEvents.IngestionBackpressureActivated)
            .Message.Should().Contain("disk free 40% (threshold 50%)");
    }

    [Fact]
    public async Task WaitUntilClearAsync_JustAboveTheThreshold_StaysHeldUntilTheReleaseMargin()
    {
        var probe = new ScriptedProbe(new HostResourceReading(5, 80));
        var time = new FakeTimeProvider();
        var logger = new FakeLogger<IngestionBackpressure>();
        var backpressure = Create(probe, time, logger, minAvailableMemoryPercent: 10);

        var waiting = backpressure.WaitUntilClearAsync(CancellationToken.None);

        // 11 % is above the 10 % threshold but inside the 3-point margin: releasing here would
        // flap the workers on and off around the line.
        probe.Reading = new HostResourceReading(11, 80);
        time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        waiting.IsCompleted.Should().BeFalse();

        probe.Reading = new HostResourceReading(14, 80);
        await AdvanceUntilAsync(time, waiting);

        Events(logger).Should().Equal(
            nameof(OpsEvents.IngestionBackpressureActivated),
            nameof(OpsEvents.IngestionBackpressureReleased));
    }

    [Fact]
    public async Task WaitUntilClearAsync_SharedByEveryWorker_AnnouncesEachTransitionOnce()
    {
        var probe = new ScriptedProbe(new HostResourceReading(5, 80));
        var time = new FakeTimeProvider();
        var logger = new FakeLogger<IngestionBackpressure>();
        var backpressure = Create(probe, time, logger, minAvailableMemoryPercent: 10);

        var workers = Enumerable.Range(0, 3).Select(_ => backpressure.WaitUntilClearAsync(CancellationToken.None)).ToList();

        probe.Reading = new HostResourceReading(50, 80);
        await AdvanceUntilAsync(time, Task.WhenAll(workers));

        Events(logger).Should().Equal(
            nameof(OpsEvents.IngestionBackpressureActivated),
            nameof(OpsEvents.IngestionBackpressureReleased));
    }

    [Fact]
    public void WaitUntilClearAsync_AboveEveryThreshold_ClaimsAtOnceAndSaysNothing()
    {
        var logger = new FakeLogger<IngestionBackpressure>();
        var backpressure = Create(new ScriptedProbe(new HostResourceReading(50, 50)), new FakeTimeProvider(), logger);

        backpressure.WaitUntilClearAsync(CancellationToken.None).IsCompletedSuccessfully.Should().BeTrue();
        logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public void WaitUntilClearAsync_OnAHostThatCannotReportAResource_AppliesNoBrakeAndSaysSoOnce()
    {
        var logger = new FakeLogger<IngestionBackpressure>();
        var backpressure = Create(new ScriptedProbe(new HostResourceReading(null, 50)), new FakeTimeProvider(), logger);

        backpressure.WaitUntilClearAsync(CancellationToken.None).IsCompletedSuccessfully.Should().BeTrue();
        backpressure.WaitUntilClearAsync(CancellationToken.None).IsCompletedSuccessfully.Should().BeTrue();

        logger.Collector.GetSnapshot().Should().ContainSingle(record => record.Level == LogLevel.Warning);
        Events(logger).Should().BeEmpty();
    }

    [Fact]
    public void WaitUntilClearAsync_Disabled_NeverReadsTheHost()
    {
        var probe = new ScriptedProbe(new HostResourceReading(1, 1));
        var backpressure = Create(probe, new FakeTimeProvider(), new FakeLogger<IngestionBackpressure>(), enabled: false);

        backpressure.WaitUntilClearAsync(CancellationToken.None).IsCompletedSuccessfully.Should().BeTrue();
        probe.Reads.Should().Be(0);
    }

    [Fact]
    public async Task WaitUntilClearAsync_Drill_HoldsForItsMinutesThenReleasesOnTheRealReadings()
    {
        var time = new FakeTimeProvider();
        var logger = new FakeLogger<IngestionBackpressure>();
        var backpressure = new IngestionBackpressure(
            new ScriptedProbe(new HostResourceReading(50, 50)),
            Microsoft.Extensions.Options.Options.Create(new IngestionBackpressureOptions { DrillMinutes = 2, ProbeIntervalSeconds = 30 }),
            time,
            logger);

        var waiting = backpressure.WaitUntilClearAsync(CancellationToken.None);
        waiting.IsCompleted.Should().BeFalse("a drill holds the claim although the host has room");

        time.Advance(TimeSpan.FromSeconds(90));
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        waiting.IsCompleted.Should().BeFalse();

        await AdvanceUntilAsync(time, waiting);

        Events(logger).Should().Equal(
            nameof(OpsEvents.IngestionBackpressureActivated),
            nameof(OpsEvents.IngestionBackpressureReleased));
        backpressure.WaitUntilClearAsync(CancellationToken.None).IsCompletedSuccessfully
            .Should().BeTrue("a drill runs once, from the first claim");
    }

    [Fact]
    public void ParseAvailableMemoryPercent_ReadsMemAvailableOverMemTotal()
    {
        const string memInfo = "MemTotal:       16000000 kB\nMemFree:          500000 kB\nMemAvailable:    4000000 kB\n";

        HostResourceProbe.ParseAvailableMemoryPercent(memInfo).Should().Be(25);
        HostResourceProbe.ParseAvailableMemoryPercent("MemTotal: 0 kB\n").Should().BeNull();
    }

    private static IEnumerable<string?> Events(FakeLogger<IngestionBackpressure> logger)
        => logger.Collector.GetSnapshot()
            .Where(record => OpsEvents.Resolve(record.Id) is not null)
            .Select(record => record.Id.Name);

    /// <summary>
    /// Moves the clock on until <paramref name="task"/> completes: the probe's next read is a
    /// timer the test cannot see being armed, and one advanced past before it exists never fires.
    /// </summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider time, Task task)
    {
        var deadline = DateTime.UtcNow + Settle;
        while (!task.IsCompleted)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the brake should have released by now");
            time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        await task;
    }

    private static IngestionBackpressure Create(
        IHostResourceProbe probe,
        TimeProvider time,
        ILogger<IngestionBackpressure> logger,
        double minAvailableMemoryPercent = 10,
        double minFreeDiskPercent = 15,
        bool enabled = true)
        => new(
            probe,
            Microsoft.Extensions.Options.Options.Create(new IngestionBackpressureOptions
            {
                Enabled = enabled,
                MinAvailableMemoryPercent = minAvailableMemoryPercent,
                MinFreeDiskPercent = minFreeDiskPercent,
                ReleaseMarginPercent = 3,
                ProbeIntervalSeconds = 30
            }),
            time,
            logger);

    private sealed class ScriptedProbe(HostResourceReading reading) : IHostResourceProbe
    {
        public HostResourceReading Reading { get; set; } = reading;

        public int Reads { get; private set; }

        public HostResourceReading Read(string diskPath)
        {
            Reads++;
            return Reading;
        }
    }
}
