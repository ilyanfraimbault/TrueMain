using AwesomeAssertions;
using Data.Pipeline;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// At boot the worker settles what its own steps' previous incarnation left behind, and only
/// those: the other lane's leftovers sit in the same tables and are live (#1513, #2474).
/// </summary>
public sealed class WorkerBootRecoveryTests
{
    [Fact]
    public async Task ExecuteAsync_RecoversOnlyTheStepsItsModeOwns()
    {
        var seed = new Recovery(PipelineCatalog.ManualSeed);
        var pattern = new Recovery(PipelineCatalog.PatternAggregation);

        await RunAsync(JobMode.FetchLane, seed, pattern);

        seed.Ran.Should().BeTrue("the fetch lane owns the manual seed");
        pattern.Ran.Should().BeFalse("the pattern fold belongs to the aggregate lane");
    }

    [Fact]
    public async Task ExecuteAsync_AFailingRecovery_NeitherStopsTheOthersNorTheBoot()
    {
        var failing = new Recovery(PipelineCatalog.ManualSeed, new InvalidOperationException("mongo down"));
        var claims = new Recovery(PipelineCatalog.MatchIngestion);

        var ran = await RunAsync(JobMode.FetchLane, failing, claims);

        claims.Ran.Should().BeTrue();
        ran.Should().BeTrue("a recovery that fails must not cost the first pass");
    }

    private static async Task<bool> RunAsync(JobMode mode, params Recovery[] recoveries)
    {
        var services = new ServiceCollection();
        var passRan = false;
        foreach (var step in PipelineCatalog.SequencedStepsFor(mode))
        {
            services.AddKeyedSingleton<IIngestorProcess>(step.Mode, new Step(() => passRan = true));
        }

        foreach (var recovery in recoveries)
        {
            services.AddSingleton<IStepBootRecovery>(recovery);
        }

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var metrics = TestIngestorMetrics.Create();
        using var worker = new Worker(
            NullLogger<Worker>.Instance,
            scopeFactory,
            Microsoft.Extensions.Options.Options.Create(new JobOptions { Mode = mode.ToString(), RunOnce = true }),
            new IterationContext(),
            new ProcessRunner(scopeFactory, new CallerContext(), metrics, NullLogger<ProcessRunner>.Instance),
            Substitute.For<IHostApplicationLifetime>(),
            metrics,
            TimeProvider.System,
            NoHeartbeatFile.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        return passRan;
    }

    private sealed class Recovery(PipelineProcess step, Exception? failure = null) : IStepBootRecovery
    {
        public bool Ran { get; private set; }

        public PipelineProcess Step => step;

        public Task RecoverAsync(CancellationToken ct)
        {
            Ran = true;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class Step(Action ran) : IIngestorProcess
    {
        public Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct)
        {
            ran();
            return Task.FromResult<IProcessRunSummary?>(null);
        }
    }
}
