using AwesomeAssertions;
using Data.Logging;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.Discovery;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// The harvest budget is a real bound on coverage, so a run that hits it must say so
/// (#495). A silent cap is how the pipeline starves without anyone noticing, so these
/// tests pin the ops events themselves — id, level and the numbers an operator needs —
/// not just the counters returned to the caller.
/// </summary>
public sealed class HarvestProcessCoverageLoggingTests
{
    [Fact]
    public async Task RunCoreAsync_WhenTheBudgetTruncatesThePool_EmitsHarvestBudgetExhausted()
    {
        var coverage = new HarvestCoverage(
            EligibleNew: 40,
            SelectedNew: 10,
            EligibleKnown: 100,
            SelectedKnown: 10,
            Platforms:
            [
                new HarvestPlatformCoverage("KR", 30, 5, 90, 8),
                new HarvestPlatformCoverage("EUW1", 10, 5, 10, 2)
            ]);
        var logger = new CapturingLogger<HarvestProcess>();

        await BuildProcess(logger, coverage).RunCoreAsync(CancellationToken.None);

        var warning = logger.Entries.Should()
            .ContainSingle(entry => entry.EventId.Id == OpsEvents.HarvestBudgetExhausted.Id).Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        // Only a registered ops event is persisted by the Mongo sink and filterable in the
        // admin Logs panel — an unregistered id would make this warning invisible there.
        OpsEvents.Resolve(warning.EventId).Should().Be(nameof(OpsEvents.HarvestBudgetExhausted));
        // The dropped counts, and the per-platform split that shows one region eating the
        // shared budget.
        warning.Message.Should().Contain("droppedNew=30");
        warning.Message.Should().Contain("droppedKnown=90");
        warning.Message.Should().Contain("KR new=5/30 known=8/90");
        warning.Message.Should().Contain("EUW1 new=5/10 known=2/10");
    }

    [Fact]
    public async Task RunCoreAsync_AlwaysReportsCoverage_OnTheCycleEvent()
    {
        var coverage = new HarvestCoverage(3, 3, 7, 7, [new HarvestPlatformCoverage("KR", 3, 3, 7, 7)]);
        var logger = new CapturingLogger<HarvestProcess>();

        var summary = await BuildProcess(logger, coverage).RunCoreAsync(CancellationToken.None);

        var completed = logger.Entries.Should()
            .ContainSingle(entry => entry.EventId.Id == OpsEvents.HarvestCycleCompleted.Id).Subject;
        completed.Level.Should().Be(LogLevel.Information);
        completed.Message.Should().Contain("eligibleNew=3");
        completed.Message.Should().Contain("selectedNew=3");
        completed.Message.Should().Contain("eligibleKnown=7");
        completed.Message.Should().Contain("selectedKnown=7");

        // A run that covered its pool must not cry wolf.
        logger.Entries.Should().NotContain(entry => entry.EventId.Id == OpsEvents.HarvestBudgetExhausted.Id);

        // The same counters ride on the recorded process-run summary (#722).
        summary.Should().NotBeNull();
        summary!.ToString().Should().Contain("BudgetExhausted = False");
    }

    private static HarvestProcess BuildProcess(ILogger<HarvestProcess> logger, HarvestCoverage coverage)
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Substitute.For<IDataSession>()));

        var harvestService = Substitute.For<IParticipantHarvestService>();
        harvestService.HarvestAsync(
                Arg.Any<IDataSession>(),
                Arg.Any<HarvestOptions>(),
                Arg.Any<ChampionCoverageSnapshot>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HarvestResult(1, 1, 1, coverage)));

        var coverageProvider = Substitute.For<IChampionCoverageProvider>();
        coverageProvider.GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ChampionCoverageSnapshot.Empty));

        return new HarvestProcess(
            logger,
            sessionFactory,
            harvestService,
            coverageProvider,
            Microsoft.Extensions.Options.Options.Create(new HarvestOptions { Platforms = ["KR"], MaxCandidatesPerRun = 20 }));
    }
}
