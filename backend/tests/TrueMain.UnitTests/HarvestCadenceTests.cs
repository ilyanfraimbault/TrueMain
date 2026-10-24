using AwesomeAssertions;
using Data.Ops.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Summaries;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// The harvest's run cadence (#2261): measured from the last run that did its work, the only
/// thing that keeps the orphan scan from following the maintenance pass's one-minute loop.
/// </summary>
public sealed class HarvestCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunCoreAsync_Skips_WhenLastRunWithinMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: Now.UtcDateTime.AddMinutes(-5));

        var summary = await harness.Process(TimeSpan.FromMinutes(20)).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<SkippedSummary>().Which.Skipped.Should().BeTrue();
        await harness.HarvestService.DidNotReceiveWithAnyArgs().HarvestAsync(default!, default!, default!, default, default);
        await harness.SessionFactory.DidNotReceiveWithAnyArgs().CreateAsync(default);
    }

    [Fact]
    public async Task RunCoreAsync_Runs_WhenLastRunOlderThanMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: Now.UtcDateTime.AddMinutes(-25));

        var summary = await harness.Process(TimeSpan.FromMinutes(20)).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<HarvestSummary>();
        await harness.HarvestService.ReceivedWithAnyArgs(1).HarvestAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task RunCoreAsync_Runs_WhenNoEarlierRunCompleted()
    {
        var harness = new Harness(lastCompletedRunUtc: null);

        var summary = await harness.Process(TimeSpan.FromMinutes(20)).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<HarvestSummary>();
    }

    [Fact]
    public async Task RunCoreAsync_RunsEveryIterationAndSkipsTheLookup_WhenMinRunIntervalIsZero()
    {
        var harness = new Harness(lastCompletedRunUtc: Now.UtcDateTime.AddSeconds(-1));

        var summary = await harness.Process(TimeSpan.Zero).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<HarvestSummary>();
        await harness.RunStore.DidNotReceiveWithAnyArgs().GetLastCompletedRunStartAsync(default!, default);
    }

    private sealed class Harness
    {
        public Harness(DateTime? lastCompletedRunUtc)
        {
            RunStore.GetLastCompletedRunStartAsync("Harvest", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(lastCompletedRunUtc));
            SessionFactory.CreateAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Substitute.For<IDataSession>()));
            HarvestService.HarvestAsync(
                    Arg.Any<IDataSession>(),
                    Arg.Any<HarvestOptions>(),
                    Arg.Any<ChampionCoverageSnapshot>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new HarvestResult(0, 0, 0, HarvestCoverage.Empty)));
            CoverageProvider.GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(ChampionCoverageSnapshot.Empty));
        }

        public IProcessRunStore RunStore { get; } = Substitute.For<IProcessRunStore>();

        public IDataSessionFactory SessionFactory { get; } = Substitute.For<IDataSessionFactory>();

        public IParticipantHarvestService HarvestService { get; } = Substitute.For<IParticipantHarvestService>();

        private IChampionCoverageProvider CoverageProvider { get; } = Substitute.For<IChampionCoverageProvider>();

        public HarvestProcess Process(TimeSpan minRunInterval)
            => new(
                NullLogger<HarvestProcess>.Instance,
                SessionFactory,
                HarvestService,
                CoverageProvider,
                RunStore,
                new FakeTimeProvider(Now),
                Microsoft.Extensions.Options.Options.Create(new HarvestOptions { Platforms = ["KR"], MinRunInterval = minRunInterval }),
                Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions()),
                Microsoft.Extensions.Options.Options.Create(new IntakeOptions()));
    }
}
