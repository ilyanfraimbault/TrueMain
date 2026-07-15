using AwesomeAssertions;
using Data.Entities;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Issue #443: a failing process must not starve the rest of the sequence.
/// Before the fix, Discovery (first in the Full sequence) failing on Riot 429
/// backoffs aborted every cycle, so nothing downstream ever ran.
/// </summary>
public sealed class WorkerProcessIsolationTests
{
    private static readonly string[] FullSequence =
    [
        "Discovery",
        "ManualSeed",
        "Harvest",
        "Scoring",
        "MatchIngestion",
        "MainAnalysis",
        "MatchParticipantEloBracketEnrichment",
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
        "ChampionPowerspikeAggregation",
        "AccountRefresh",
        "MatchDataRetention"
    ];

    [Fact]
    public async Task ExecuteAsync_RunsRemainingProcesses_WhenFirstProcessThrows()
    {
        var executed = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(new ThrowingProcess("Discovery"));
        foreach (var name in FullSequence.Skip(1))
        {
            services.AddSingleton<IIngestorProcess>(new RecordingProcess(name, executed));
        }

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildFullModeWorker(services, lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Every process after the thrower still ran, in order.
        executed.Should().Equal(FullSequence.Skip(1));
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ExecuteAsync_RecordsFailedRunForThrower_AndRunsTheRest()
    {
        var executed = new List<string>();
        var recorder = Substitute.For<IProcessRunRecorder>();
        // The run id from RecordStartAsync must be threaded through to RecordAsync.
        // Pin a specific id and assert it below (rather than Arg.Any<Guid>) so an
        // implementation that minted a fresh Guid for the terminal write would fail.
        var expectedRunId = Guid.NewGuid();
        recorder
            .RecordStartAsync("Discovery", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(expectedRunId);
        var services = new ServiceCollection();

        // Wrap the thrower exactly like production does (RecordedProcess persists
        // the Failed run before rethrowing into the worker loop).
        services.AddSingleton<IIngestorProcess>(
            new RecordedProcess<ThrowingProcess>(
                new ThrowingProcess("Discovery"),
                recorder,
                TimeProvider.System,
                NullLogger<RecordedProcess<ThrowingProcess>>.Instance));
        foreach (var name in FullSequence.Skip(1))
        {
            services.AddSingleton<IIngestorProcess>(new RecordingProcess(name, executed));
        }

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildFullModeWorker(services, lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        await recorder.Received(1).RecordStartAsync(
            "Discovery",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());

        await recorder.Received(1).RecordAsync(
            expectedRunId,
            "Discovery",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Failed,
            null,
            "simulated process failure",
            Arg.Any<CancellationToken>());
        executed.Should().Equal(FullSequence.Skip(1));
    }

    [Fact]
    public async Task ExecuteAsync_StampsOneSharedIterationId_AcrossEveryProcessInThePass()
    {
        // Every process in a single pass must observe the SAME, non-null iteration
        // id (the Worker opens it once for the whole sequence), so their recorded
        // runs group into one chain. A failing process must not break the grouping
        // for the rest, so include a thrower in the middle.
        var iterationContext = new IterationContext();
        var observed = new List<Guid?>();
        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(
            new IterationObservingProcess("Discovery", iterationContext, observed));
        services.AddSingleton<IIngestorProcess>(new ThrowingProcess("ManualSeed"));
        foreach (var name in FullSequence.Skip(2))
        {
            services.AddSingleton<IIngestorProcess>(
                new IterationObservingProcess(name, iterationContext, observed));
        }

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildFullModeWorker(services, iterationContext, lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Seven non-thrower processes each observed an iteration id.
        observed.Should().HaveCount(FullSequence.Length - 1);
        observed.Should().AllSatisfy(id => id.Should().NotBeNull());
        observed.Distinct().Should().ContainSingle("every process shares the pass's id");
        observed[0].Should().NotBe(Guid.Empty);

        // The id is scoped to the pass: once it ends, the context is back to null.
        iterationContext.CurrentIterationId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation_WhenAProcessObservesShutdown()
    {
        using var cts = new CancellationTokenSource();
        var executed = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(new CancellingProcess("Discovery", cts));
        foreach (var name in FullSequence.Skip(1))
        {
            services.AddSingleton<IIngestorProcess>(new RecordingProcess(name, executed));
        }

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildFullModeWorker(services, lifetime);

        await worker.StartAsync(cts.Token);

        // Cooperative cancellation must keep propagating instead of being treated
        // as a per-process failure, so the host observes the natural shutdown flow.
        var act = async () => await worker.ExecuteTask!;
        await act.Should().ThrowAsync<OperationCanceledException>();

        executed.Should().BeEmpty();
        lifetime.DidNotReceive().StopApplication();
    }

    private static Worker BuildFullModeWorker(ServiceCollection services, IHostApplicationLifetime lifetime)
        => BuildFullModeWorker(services, new IterationContext(), lifetime);

    private static Worker BuildFullModeWorker(
        ServiceCollection services,
        IIterationContext iterationContext,
        IHostApplicationLifetime lifetime)
    {
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = "Full",
            RunOnce = true
        });

        return new Worker(
            NullLogger<Worker>.Instance, scopeFactory, jobOptions, iterationContext, lifetime);
    }

    private sealed class RecordingProcess(string name, List<string> executed) : IIngestorProcess
    {
        public string Name => name;

        public async Task<object?> RunCoreAsync(CancellationToken ct)
        {
            await Task.Yield();
            executed.Add(name);
            return null;
        }
    }

    // Records the iteration id visible on its async flow when it runs, so the test
    // can assert every process in the pass shares one non-null id.
    private sealed class IterationObservingProcess(
        string name,
        IIterationContext iterationContext,
        List<Guid?> observed) : IIngestorProcess
    {
        public string Name => name;

        public async Task<object?> RunCoreAsync(CancellationToken ct)
        {
            await Task.Yield();
            observed.Add(iterationContext.CurrentIterationId);
            return null;
        }
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
