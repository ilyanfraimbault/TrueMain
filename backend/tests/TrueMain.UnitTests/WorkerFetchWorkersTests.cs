using System.Collections.Concurrent;
using AwesomeAssertions;
using Data.Pipeline;
using Ingestor;
using Ingestor.FetchWorkers;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Summaries;
using Ingestor.Riot.RateLimiting;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// <see cref="JobMode.FetchWorkers"/> through the real <see cref="Worker"/> (#1457): match
/// fetching runs as permanent workers beside a maintenance pass of the lane's other steps,
/// the boot still frees what a crash left claimed, and <c>Job:RunOnce</c> keeps meaning one
/// pass of everything the lane owns.
/// </summary>
public sealed class WorkerFetchWorkersTests
{
    [Fact]
    public async Task ExecuteAsync_RunOnce_RunsTheMaintenancePassAndOneBatchPerRouteThenStops()
    {
        var harness = new Harness(["EUW1", "KR", "NA1"]);
        using var worker = harness.BuildWorker(runOnce: true);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Maintenance.Should().Equal(
            PipelineCatalog.InLane(PipelineLane.Fetch).Where(step => step != PipelineCatalog.MatchIngestion).Select(step => step.Mode),
            "the maintenance pass is the fetch lane without the step the workers took over");
        harness.Batches.Should().BeEquivalentTo(["NA1", "KR", "EUW1"], "one batch per routing value, each on its own platforms");
        harness.Lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ExecuteAsync_AtBoot_ReleasesWhatTheDeadIncarnationLeftClaimedBeforeAnyWorkerClaims()
    {
        // Crash/restart: a single instance per lane cannot hold a live claim at boot, so the
        // claims an OOM kill left behind are freed before the first worker batch, as they
        // are for the sequential fetch lane (#1513).
        var harness = new Harness(["EUW1"]);
        var releasedFirst = false;
        harness.Claims.ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                releasedFirst = harness.Batches.IsEmpty;
                return new ExpiredClaimRelease(3, 1);
            });
        using var worker = harness.BuildWorker(runOnce: true);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        await harness.Claims.Received(1).ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>());
        releasedFirst.Should().BeTrue("a release after the first claim would free the batch it had just taken");
    }

    [Fact]
    public async Task StopAsync_Permanent_StopsTheWorkersWithTheHost()
    {
        var harness = new Harness(["EUW1"]);
        using var worker = harness.BuildWorker(runOnce: false);

        await worker.StartAsync(CancellationToken.None);
        await harness.WaitForBatchAsync();
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        worker.ExecuteTask!.IsCompleted.Should().BeTrue("the host's stop reaches the workers, not only the maintenance loop");
        harness.Lifetime.DidNotReceive().StopApplication();
    }

    private sealed class Harness
    {
        private readonly ServiceCollection _services = new();
        private readonly IterationContext _iterations = new();
        private readonly CallerContext _callers = new();

        public Harness(List<string> platforms)
        {
            Claims.ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>()).Returns(new ExpiredClaimRelease(0, 0));
            foreach (var step in PipelineCatalog.SequencedStepsFor(JobMode.FetchWorkers).Select(step => step.Mode))
            {
                _services.AddKeyedSingleton<IIngestorProcess>(step, new MaintenanceStep(step, Maintenance));
            }

            _services.AddKeyedScoped<IIngestorProcess>(JobMode.MatchIngestionOnly, (_, _) => new FetchBatch(Batches));
            _services.AddSingleton(Claims);
            _services.AddLogging();
            _services.AddScoped<IStepBootRecovery, MatchClaimBootRecovery>();
            _services.AddSingleton(provider => new ProcessRunner(
                provider.GetRequiredService<IServiceScopeFactory>(),
                _callers,
                TestIngestorMetrics.Create(),
                NullLogger<ProcessRunner>.Instance));
            _services.AddSingleton(provider => new MatchFetchWorkerPool(
                provider.GetRequiredService<IServiceScopeFactory>(),
                _iterations,
                provider.GetRequiredService<ProcessRunner>(),
                Substitute.For<IRiotRateLimiter>(),
                MatchFetchWorkerPoolTests.Unpressured(),
                Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions { Platforms = platforms }),
                Microsoft.Extensions.Options.Options.Create(new MatchFetchWorkersOptions()),
                TimeProvider.System,
                NullLogger<MatchFetchWorkerPool>.Instance));
        }

        public IMatchClaimService Claims { get; } = Substitute.For<IMatchClaimService>();

        public IHostApplicationLifetime Lifetime { get; } = Substitute.For<IHostApplicationLifetime>();

        public List<JobMode> Maintenance { get; } = [];

        public ConcurrentQueue<string> Batches { get; } = new();

        public async Task WaitForBatchAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (Batches.IsEmpty)
            {
                DateTime.UtcNow.Should().BeBefore(deadline, "a fetch worker should have run a batch by now");
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
        }

        public Worker BuildWorker(bool runOnce)
        {
            var provider = _services.BuildServiceProvider();
            return new Worker(
                NullLogger<Worker>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Options.Options.Create(new JobOptions
                {
                    Mode = nameof(JobMode.FetchWorkers),
                    RunOnce = runOnce,
                    IntervalMinutes = runOnce ? null : 60
                }),
                _iterations,
                provider.GetRequiredService<ProcessRunner>(),
                Lifetime,
                TestIngestorMetrics.Create(),
                TimeProvider.System,
                NoHeartbeatFile.Instance);
        }
    }

    private sealed class MaintenanceStep(JobMode step, List<JobMode> executed) : IIngestorProcess
    {
        public string Name => step.ToString();

        public Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct)
        {
            executed.Add(step);
            return Task.FromResult<IProcessRunSummary?>(null);
        }
    }

    /// <summary>A batch that found nothing, so a permanent worker parks on its idle delay.</summary>
    private sealed class FetchBatch(ConcurrentQueue<string> batches) : IIngestorProcess
    {
        public Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct)
        {
            batches.Enqueue(string.Join('+', input.Platforms!));
            return Task.FromResult<IProcessRunSummary?>(new MatchIngestionSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, []));
        }
    }
}
