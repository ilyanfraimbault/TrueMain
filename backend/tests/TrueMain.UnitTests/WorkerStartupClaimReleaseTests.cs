using AwesomeAssertions;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Claims held at boot belong to the incarnation that just died, and the worker frees them
/// before its first pass (#1513).
///
/// <para>
/// Only the lease could settle them before: an account a redeploy interrupted stayed
/// <c>Processing</c> for the rest of <c>MatchIngestion:ClaimLeaseMinutes</c>, so the deploy
/// cost the pipeline the pass it interrupted plus whatever was left of the lease. The
/// reasoning is the same one that lets the worker abandon orphaned runs at startup — one
/// instance per lane — which is also why the release is scoped to the lane that claims.
/// </para>
/// </summary>
public sealed class WorkerStartupClaimReleaseTests
{
    [Fact]
    public async Task ExecuteAsync_OnTheLaneThatClaims_ReleasesOrphanedClaimsBeforeTheFirstPass()
    {
        var released = false;
        var claimService = Substitute.For<IMatchClaimService>();
        claimService.ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                released = true;
                return new ExpiredClaimRelease(4, 2);
            });
        var process = new RecordingProcess(() => released);

        using var worker = BuildWorker(JobMode.MatchIngestionOnly, claimService, process);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        await claimService.Received(1).ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>());
        process.ReleasedBeforeItRan.Should().BeTrue(
            "a release that ran after the claim would free the accounts this pass had just taken");
    }

    [Fact]
    public async Task ExecuteAsync_OnALaneThatDoesNotClaim_LeavesTheClaimsAlone()
    {
        // The aggregate lane restarts on its own schedule, and the claims on the table are
        // the fetch lane's live ones. Releasing them from here would hand every account it
        // is working on to a second claim.
        var claimService = Substitute.For<IMatchClaimService>();

        using var worker = BuildWorker(JobMode.PatternAggregationOnly, claimService, new RecordingProcess(() => false));

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        await claimService.DidNotReceive().ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheReleaseFails_StillRunsThePass()
    {
        // The lease-based reap at the top of MatchIngestion is the fallback that was doing
        // this job alone, so a store that is unreachable at boot must not cost a pass.
        var claimService = Substitute.For<IMatchClaimService>();
        claimService.ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>())
            .Returns<ExpiredClaimRelease>(_ => throw new InvalidOperationException("database unreachable"));
        var process = new RecordingProcess(() => false);

        using var worker = BuildWorker(JobMode.MatchIngestionOnly, claimService, process);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        process.Ran.Should().BeTrue();
    }

    private static Worker BuildWorker(JobMode mode, IMatchClaimService claimService, RecordingProcess process)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IIngestorProcess>(mode, process);
        services.AddSingleton(claimService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = mode.ToString(),
            RunOnce = true
        });

        return new Worker(
            NullLogger<Worker>.Instance,
            scopeFactory,
            jobOptions,
            new IterationContext(),
            new CallerContext(),
            Substitute.For<IHostApplicationLifetime>(),
            TestIngestorMetrics.Create(),
            TimeProvider.System,
            NoHeartbeatFile.Instance);
    }

    /// <summary>
    /// Reads the release's state as the pass starts, which is what pins the ordering: a
    /// release that ran after the claim would free the accounts the pass had just taken.
    /// </summary>
    private sealed class RecordingProcess(Func<bool> releaseHappened) : IIngestorProcess
    {
        public bool Ran { get; private set; }

        public bool ReleasedBeforeItRan { get; private set; }

        public string Name => "Recording";

        public Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
        {
            Ran = true;
            ReleasedBeforeItRan = releaseHappened();
            return Task.FromResult<IProcessRunSummary?>(null);
        }
    }
}
