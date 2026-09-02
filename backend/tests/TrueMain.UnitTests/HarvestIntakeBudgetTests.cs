using AwesomeAssertions;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.Discovery;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// The harvest budget the run actually spends (#1361): its refresh half is bounded by what
/// the claim can absorb, its discovery half is not. Asserted on the <see cref="HarvestOptions"/>
/// handed to the service, because that re-sized instance — a smaller budget plus a larger
/// <c>NewCandidateShare</c> — is the whole mechanism.
/// </summary>
public sealed class HarvestIntakeBudgetTests
{
    [Fact]
    public async Task RunCoreAsync_ShrinksOnlyTheRefreshHalfOfTheBudget()
    {
        var harness = new Harness(
            harvest: new HarvestOptions { Platforms = ["KR"], MaxCandidatesPerRun = 7500, NewCandidateShare = 0.5 },
            matchIngestion: new MatchIngestionOptions { BatchSize = 75, EstablishedMainShare = 0.7 },
            intake: new IntakeOptions { PromotionHeadroomFactor = 3 });

        await harness.Process.RunCoreAsync(CancellationToken.None);

        // Discovery keeps its configured 3 750; the refresh half drops from 3 750 to the
        // claim's 22 new slots x 3 cycles of headroom = 66.
        var spent = harness.CapturedOptions;
        spent.MaxCandidatesPerRun.Should().Be(3816);
        (spent.MaxCandidatesPerRun * spent.NewCandidateShare).Should().BeApproximately(3750, 1);

        // The configured instance is left untouched — the re-sizing is per run, not a mutation
        // of the bound options.
        harness.Configured.MaxCandidatesPerRun.Should().Be(7500);
        harness.Configured.NewCandidateShare.Should().Be(0.5);
    }

    [Fact]
    public async Task RunCoreAsync_LeavesAnAlreadySmallBudgetAlone()
    {
        var harness = new Harness(
            harvest: new HarvestOptions { Platforms = ["KR"], MaxCandidatesPerRun = 20, NewCandidateShare = 0.5 },
            matchIngestion: new MatchIngestionOptions { BatchSize = 75, EstablishedMainShare = 0.7 },
            intake: new IntakeOptions { PromotionHeadroomFactor = 3 });

        await harness.Process.RunCoreAsync(CancellationToken.None);

        // 10 new + min(10, 66) refresh = 20, i.e. the configured budget: the cap only ever
        // lowers, and a configuration already below the claim's capacity is passed through
        // unchanged rather than re-derived into something larger.
        harness.CapturedOptions.MaxCandidatesPerRun.Should().Be(20);
        harness.CapturedOptions.NewCandidateShare.Should().Be(0.5);
    }

    [Fact]
    public async Task RunCoreAsync_CarriesEveryOtherHarvestSettingThrough()
    {
        var configured = new HarvestOptions
        {
            Platforms = ["KR", "EUW1"],
            QueueId = 440,
            MinObservedGames = 7,
            MaxCandidatesPerRun = 7500,
            NewCandidateShare = 0.5,
            LookbackDays = 14,
            SaveBatchSize = 42
        };
        var harness = new Harness(
            configured,
            new MatchIngestionOptions { BatchSize = 75, EstablishedMainShare = 0.7 },
            new IntakeOptions { PromotionHeadroomFactor = 3 });

        await harness.Process.RunCoreAsync(CancellationToken.None);

        // Only the two budget fields are re-derived; a copy that silently reset the scan
        // window or the queue would change what the harvest looks at, not how much of it.
        var spent = harness.CapturedOptions;
        spent.Platforms.Should().Equal("KR", "EUW1");
        spent.QueueId.Should().Be(440);
        spent.MinObservedGames.Should().Be(7);
        spent.LookbackDays.Should().Be(14);
        spent.SaveBatchSize.Should().Be(42);
    }

    private sealed class Harness
    {
        public Harness(HarvestOptions harvest, MatchIngestionOptions matchIngestion, IntakeOptions intake)
        {
            Configured = harvest;

            var sessionFactory = Substitute.For<IDataSessionFactory>();
            sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Substitute.For<IDataSession>()));

            var harvestService = Substitute.For<IParticipantHarvestService>();
            harvestService.HarvestAsync(
                    Arg.Any<IDataSession>(),
                    Arg.Do<HarvestOptions>(options => CapturedOptions = options),
                    Arg.Any<ChampionCoverageSnapshot>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new HarvestResult(0, 0, 0, HarvestCoverage.Empty)));

            var coverageProvider = Substitute.For<IChampionCoverageProvider>();
            coverageProvider.GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(ChampionCoverageSnapshot.Empty));

            Process = new HarvestProcess(
                NullLogger<HarvestProcess>.Instance,
                sessionFactory,
                harvestService,
                coverageProvider,
                TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(harvest),
                Microsoft.Extensions.Options.Options.Create(matchIngestion),
                Microsoft.Extensions.Options.Options.Create(intake));
        }

        public HarvestProcess Process { get; }

        public HarvestOptions Configured { get; }

        public HarvestOptions CapturedOptions { get; private set; } = null!;
    }
}
