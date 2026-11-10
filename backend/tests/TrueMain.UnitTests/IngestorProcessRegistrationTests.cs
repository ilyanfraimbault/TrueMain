using AwesomeAssertions;
using Data.Pipeline;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class IngestorProcessRegistrationTests
{
    [Fact]
    public void AddIngestorProcesses_RegistersOneProcessPerCatalogStep_AndNothingUnderACompositeMode()
    {
        var services = new ServiceCollection();
        services.AddIngestorProcesses();

        var keys = services
            .Where(d => d.ServiceType == typeof(IIngestorProcess) && d.IsKeyedService)
            .Select(d => d.ServiceKey)
            .OfType<JobMode>()
            .ToList();

        keys.Should().OnlyHaveUniqueItems();
        keys.Should().BeEquivalentTo(PipelineCatalog.Steps.Select(step => step.Mode));
        keys.Should().NotIntersectWith(PipelineCatalog.CompositeModes);
    }

    [Fact]
    public void EveryDeclaredMode_IsACatalogStepOrAComposite()
    {
        Enum.GetValues<JobMode>().Should().BeEquivalentTo(
            PipelineCatalog.Steps.Select(step => step.Mode).Concat(PipelineCatalog.CompositeModes));
    }

    [Fact]
    public void AddRecordedProcess_ResolvesTheRecordedWrapper_ByKeyFromAScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IProcessRunRecorder>());
        services.AddRecordedProcess<StubProcess>(PipelineCatalog.Discovery);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        var process = scope.ServiceProvider.GetRequiredKeyedService<IIngestorProcess>(JobMode.DiscoveryOnly);

        process.Should().BeOfType<RecordedProcess<StubProcess>>();
        otherScope.ServiceProvider.GetRequiredKeyedService<IIngestorProcess>(JobMode.DiscoveryOnly)
            .Should().NotBeSameAs(process);
    }

    [Fact]
    public async Task RecordedProcess_RecordsTheRunUnderTheCatalogName()
    {
        var recorder = Substitute.For<IProcessRunRecorder>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(recorder);
        services.AddRecordedProcess<StubProcess>(PipelineCatalog.MatchupAggregation);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredKeyedService<IIngestorProcess>(JobMode.MatchupLeadAggregationOnly)
            .RunCoreAsync(RunInput.Whole, CancellationToken.None);

        await recorder.Received(1).RecordStartAsync("ChampionMatchupLeadAggregation", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Steps_HaveUniquePersistedNames()
        => PipelineCatalog.Steps.Select(step => step.Name).Should().OnlyHaveUniqueItems();

    [Fact]
    public void EveryStep_RunsAfterWhatItReads()
    {
        var order = PipelineCatalog.Steps.Select(step => step.Mode).ToList();

        foreach (var step in PipelineCatalog.Steps)
        {
            foreach (var prerequisite in step.RunsAfter)
            {
                order.IndexOf(prerequisite).Should().BeLessThan(
                    order.IndexOf(step.Mode), $"{step.Name} reads what {prerequisite} writes");
            }
        }
    }

    [Theory]
    [InlineData(JobMode.FetchLane)]
    [InlineData(JobMode.FetchWorkers)]
    public void TheFetchModes_OwnTheFetchLane(JobMode mode)
        => PipelineCatalog.StepsOwnedBy(mode).Should().Equal(PipelineCatalog.InLane(PipelineLane.Fetch));

    [Fact]
    public void FetchWorkers_SequencesEveryFetchStepButMatchFetching()
    {
        PipelineCatalog.SequencedStepsFor(JobMode.FetchWorkers)
            .Should().Equal(PipelineCatalog.InLane(PipelineLane.Fetch).Where(step => step != PipelineCatalog.MatchIngestion));
        PipelineCatalog.SequencedStepsFor(JobMode.FetchLane)
            .Should().Equal(PipelineCatalog.InLane(PipelineLane.Fetch), "the lane keeps its sequential pass");
    }

    [Fact]
    public void ASingleProcessMode_OwnsItsStepOnly()
        => PipelineCatalog.StepsOwnedBy(JobMode.HarvestOnly).Should().Equal(PipelineCatalog.Harvest);

    [Fact]
    public void ModesRunning_NamesEveryCompositeThatRunsTheLane()
    {
        PipelineCatalog.ModesRunning(PipelineLane.Fetch)
            .Should().BeEquivalentTo([JobMode.Full, JobMode.FetchLane, JobMode.FetchWorkers]);
        PipelineCatalog.ModesRunning(PipelineLane.Aggregate)
            .Should().BeEquivalentTo([JobMode.Full, JobMode.AggregateLane]);
    }

    [Fact]
    public void StepsOwnedBy_Throws_ForAnUndefinedMode()
    {
        var act = () => PipelineCatalog.StepsOwnedBy((JobMode)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class StubProcess : IIngestorProcess
    {
        public Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct)
            => Task.FromResult<IProcessRunSummary?>(null);
    }
}
