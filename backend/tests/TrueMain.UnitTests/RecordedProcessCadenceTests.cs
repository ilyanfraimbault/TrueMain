using System.Reflection;
using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Data.Pipeline;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// The cadence guard (#487, #1149) is applied once, by the recording decorator, to every
/// <see cref="ICadencedProcess"/>: measured from the last run that did its work, and the
/// declined pass recorded Skipped without the step being called.
/// </summary>
public sealed class RecordedProcessCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task RunCoreAsync_WithinTheInterval_RecordsASkipWithoutCallingTheStep()
    {
        var harness = new Harness(lastCompletedRun: Now.UtcDateTime.AddHours(-1));

        var summary = await harness.Run(TimeSpan.FromDays(1));

        harness.Step.Calls.Should().Be(0);
        summary.Should().BeOfType<SkippedSummary>()
            .Which.Reason.Should().Be("Within MinRunInterval; discovery skipped this iteration.");
        await harness.Recorder.Received(1).RecordAsync(
            Arg.Any<Guid>(),
            PipelineCatalog.Discovery.Name,
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Skipped,
            Arg.Any<IProcessRunSummary>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_PastTheInterval_RunsTheStep()
    {
        var harness = new Harness(lastCompletedRun: Now.UtcDateTime.AddDays(-2));

        await harness.Run(TimeSpan.FromDays(1));

        harness.Step.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunCoreAsync_WithNoEarlierRun_RunsTheStep()
    {
        var harness = new Harness(lastCompletedRun: null);

        await harness.Run(TimeSpan.FromDays(1));

        harness.Step.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunCoreAsync_WithAZeroInterval_RunsEveryPassWithoutLookingUp()
    {
        var harness = new Harness(lastCompletedRun: Now.UtcDateTime);

        await harness.Run(TimeSpan.Zero);

        harness.Step.Calls.Should().Be(1);
        await harness.Store.DidNotReceive().GetLastCompletedRunStartAsync(
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheCadencedSteps_AreTheOnesWithAMinRunInterval()
        => typeof(ICadencedProcess).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false } && typeof(ICadencedProcess).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<PipelineStepAttribute>()!.Mode)
            .Should().BeEquivalentTo(
            [
                JobMode.DiscoveryOnly,
                JobMode.HarvestOnly,
                JobMode.MainActivityOnly,
                JobMode.LadderSyncOnly,
                JobMode.PaceSamplingOnly,
                JobMode.ChampionGameCountRefreshOnly
            ]);

    private sealed class Harness
    {
        public Harness(DateTime? lastCompletedRun)
            => Store.GetLastCompletedRunStartAsync(PipelineCatalog.Discovery.Name, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(lastCompletedRun));

        public IProcessRunRecorder Recorder { get; } = Substitute.For<IProcessRunRecorder>();

        public IProcessRunStore Store { get; } = Substitute.For<IProcessRunStore>();

        public CadencedStep Step { get; private set; } = new(TimeSpan.Zero);

        public Task<IProcessRunSummary?> Run(TimeSpan interval)
        {
            Step = new CadencedStep(interval);
            return new RecordedProcess<CadencedStep>(
                    Step,
                    PipelineCatalog.Discovery,
                    Recorder,
                    Store,
                    new FakeTimeProvider(Now),
                    NullLogger<RecordedProcess<CadencedStep>>.Instance)
                .RunCoreAsync(RunInput.Whole, CancellationToken.None);
        }
    }

    internal sealed class CadencedStep(TimeSpan interval) : ICadencedProcess
    {
        public int Calls { get; private set; }

        public TimeSpan MinRunInterval => interval;

        public Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IProcessRunSummary?>(new NoWorkSummary("ran", 0));
        }
    }
}
