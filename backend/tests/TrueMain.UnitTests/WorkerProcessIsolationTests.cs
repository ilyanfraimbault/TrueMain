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
        "Scoring",
        "MatchIngestion",
        "MainAnalysis",
        "ChampionPatternAggregation",
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
        var services = new ServiceCollection();

        // Wrap the thrower exactly like production does (RecordedProcess persists
        // the Failed run before rethrowing into the worker loop).
        services.AddSingleton<IIngestorProcess>(
            new RecordedProcess<ThrowingProcess>(new ThrowingProcess("Discovery"), recorder));
        foreach (var name in FullSequence.Skip(1))
        {
            services.AddSingleton<IIngestorProcess>(new RecordingProcess(name, executed));
        }

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var worker = BuildFullModeWorker(services, lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        await recorder.Received(1).RecordAsync(
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
    {
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = "Full",
            RunOnce = true
        });

        return new Worker(NullLogger<Worker>.Instance, scopeFactory, jobOptions, lifetime);
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
