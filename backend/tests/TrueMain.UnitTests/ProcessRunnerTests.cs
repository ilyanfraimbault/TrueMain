using AwesomeAssertions;
using Data.Pipeline;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.UnitTests;

/// <summary>
/// The harness both the sequenced lanes and the permanent fetch workers run a step through.
/// </summary>
public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_HandsTheProcessItsInput_UnderTheStepsCallerName()
    {
        var callers = new CallerContext();
        var process = new Probe(callers);
        var input = RunInput.Narrowed(["EUW1"], 3);

        var run = await Runner(process, callers).RunAsync(PipelineCatalog.MatchIngestion, JobMode.FetchWorkers, input, CancellationToken.None);

        run.IsFailed.Should().BeFalse();
        run.Summary.Should().BeOfType<NoWorkSummary>();
        process.SeenInput.Should().BeSameAs(input);
        process.SeenCaller.Should().Be(PipelineCatalog.MatchIngestion.Name);
        callers.CurrentCaller.Should().BeNull("the caller name must not leak past the run");
    }

    [Fact]
    public async Task RunAsync_AFailingStep_IsReturnedAsFailedInsteadOfThrown()
    {
        var callers = new CallerContext();
        var process = new Probe(callers, new InvalidOperationException("riot down"));

        var run = await Runner(process, callers).RunAsync(PipelineCatalog.MatchIngestion, JobMode.FetchLane, RunInput.Whole, CancellationToken.None);

        run.IsFailed.Should().BeTrue();
        run.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("riot down");
    }

    [Fact]
    public async Task RunAsync_AShutdown_PropagatesInsteadOfCountingAsAFailure()
    {
        var callers = new CallerContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var process = new Probe(callers, new OperationCanceledException(cts.Token));

        var act = () => Runner(process, callers).RunAsync(PipelineCatalog.MatchIngestion, JobMode.FetchLane, RunInput.Whole, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_AStepWithNoProcess_IsAWiringFaultNamingTheStep()
    {
        var runner = new ProcessRunner(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new CallerContext(),
            TestIngestorMetrics.Create(),
            NullLogger<ProcessRunner>.Instance);

        var act = () => runner.RunAsync(PipelineCatalog.Scoring, JobMode.FetchLane, RunInput.Whole, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(JobMode.ScoringOnly));
    }

    private static ProcessRunner Runner(Probe process, ICallerContext callers)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IIngestorProcess>(PipelineCatalog.MatchIngestion.Mode, process);
        return new ProcessRunner(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            callers,
            TestIngestorMetrics.Create(),
            NullLogger<ProcessRunner>.Instance);
    }

    private sealed class Probe(ICallerContext callers, Exception? failure = null) : IIngestorProcess
    {
        public RunInput? SeenInput { get; private set; }

        public string? SeenCaller { get; private set; }

        public Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct)
        {
            SeenInput = input;
            SeenCaller = callers.CurrentCaller;
            return failure is null
                ? Task.FromResult<IProcessRunSummary?>(new NoWorkSummary("probe", 0))
                : Task.FromException<IProcessRunSummary?>(failure);
        }
    }
}
