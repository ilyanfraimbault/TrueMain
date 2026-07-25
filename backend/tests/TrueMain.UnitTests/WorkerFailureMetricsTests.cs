using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Issue #260: the worker swallows every ingestion failure and only logs it, so a dead
/// Riot key or schema drift loops forever with nothing alertable. Each swallowed failure
/// must also increment the "ingestor.run.failures" counter, tagged by process and mode —
/// and a graceful shutdown must stay out of it (#255).
/// </summary>
public sealed class WorkerFailureMetricsTests
{
    private static readonly string[] FullSequence =
    [
        "Discovery",
        "ManualSeed",
        "Harvest",
        "Scoring",
        "MatchIngestion",
        "MatchTeamPositionCorrection",
        "MainAnalysis",
        "MatchParticipantEloBracketEnrichment",
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
        "ChampionPowerspikeAggregation",
        "AccountRefresh",
        "MatchDataRetention"
    ];

    private static readonly string[] ThrowingProcessNames = ["Discovery", "MatchIngestion"];

    [Fact]
    public async Task ExecuteAsync_IncrementsFailureCounter_TaggedWithProcessAndMode_WhenAProcessThrows()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory, IngestorMetrics.MeterName, IngestorMetrics.RunFailuresCounterName);

        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(new ThrowingProcess("Discovery"));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildWorker(services, meterFactory, "DiscoveryOnly", lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        var measurement = collector.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags["process"].Should().Be("Discovery");
        measurement.Tags["mode"].Should().Be("DiscoveryOnly");
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ExecuteAsync_IncrementsFailureCounterOncePerFailingProcess_WhenRunningFullSequence()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory, IngestorMetrics.MeterName, IngestorMetrics.RunFailuresCounterName);

        // Two failures in a full pass: the counter must attribute each one to its own
        // process rather than collapsing the pass into a single increment.
        var services = new ServiceCollection();
        foreach (var name in FullSequence)
        {
            services.AddSingleton<IIngestorProcess>(
                ThrowingProcessNames.Contains(name, StringComparer.Ordinal)
                    ? new ThrowingProcess(name)
                    : new NoOpProcess(name));
        }

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildWorker(services, meterFactory, "Full", lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        var measurements = collector.GetMeasurementSnapshot();
        measurements.Should().HaveCount(2);
        measurements.Select(m => m.Tags["process"] as string).Should().Equal("Discovery", "MatchIngestion");
        measurements.Should().AllSatisfy(m =>
        {
            m.Value.Should().Be(1);
            m.Tags["mode"].Should().Be("Full");
        });
    }

    [Fact]
    public async Task ExecuteAsync_LeavesFailureCounterUntouched_WhenEveryProcessSucceeds()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory, IngestorMetrics.MeterName, IngestorMetrics.RunFailuresCounterName);

        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(new NoOpProcess("Discovery"));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildWorker(services, meterFactory, "DiscoveryOnly", lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        collector.GetMeasurementSnapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_LeavesFailureCounterUntouched_WhenShutdownCancelsTheRun()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory, IngestorMetrics.MeterName, IngestorMetrics.RunFailuresCounterName);

        using var cts = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(new CancellingProcess("Discovery", cts));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildWorker(services, meterFactory, "DiscoveryOnly", lifetime);

        await worker.StartAsync(cts.Token);

        var act = async () => await worker.ExecuteTask!;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // A graceful shutdown is not a pipeline failure: counting it would make every
        // redeploy fire the alert this counter exists to feed.
        collector.GetMeasurementSnapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_TagsTheFailureAsWholeRun_WhenTheRunFailsOutsideAnyProcess()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory, IngestorMetrics.MeterName, IngestorMetrics.RunFailuresCounterName);

        // No process registered under the configured mode's name: the run blows up while
        // building the process index, before any single process can be blamed.
        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(new NoOpProcess("Scoring"));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildWorker(services, meterFactory, "DiscoveryOnly", lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        var measurement = collector.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags["process"].Should().Be(IngestorMetrics.WholeRunProcess);
        measurement.Tags["mode"].Should().Be("DiscoveryOnly");
    }

    private static Worker BuildWorker(
        ServiceCollection services,
        IMeterFactory meterFactory,
        string mode,
        IHostApplicationLifetime lifetime)
    {
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = mode,
            RunOnce = true
        });

        return new Worker(
            NullLogger<Worker>.Instance,
            scopeFactory,
            jobOptions,
            new IterationContext(),
            lifetime,
            new IngestorMetrics(meterFactory));
    }

    private sealed class NoOpProcess(string name) : IIngestorProcess
    {
        public string Name => name;

        public Task<object?> RunCoreAsync(CancellationToken ct) => Task.FromResult<object?>(null);
    }

    private sealed class ThrowingProcess(string name) : IIngestorProcess
    {
        public string Name => name;

        public async Task<object?> RunCoreAsync(CancellationToken ct)
        {
            await Task.Yield();
            throw new InvalidOperationException("simulated process failure");
        }
    }

    private sealed class CancellingProcess(string name, CancellationTokenSource cts) : IIngestorProcess
    {
        public string Name => name;

        public async Task<object?> RunCoreAsync(CancellationToken ct)
        {
            await Task.Yield();
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }
}
