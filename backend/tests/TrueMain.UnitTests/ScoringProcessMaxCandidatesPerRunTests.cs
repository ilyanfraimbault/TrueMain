using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// #1233 / ING-13: the scoring drain is capped per run like every other process.
/// Measured by the number of candidate rows the process pulls before it stops —
/// harvest refills the <c>New</c> backlog on every cycle, so an uncapped drain can
/// spend a whole ingestor tick scoring rows the next run would have re-scored anyway.
/// </summary>
public sealed class ScoringProcessMaxCandidatesPerRunTests
{
    [Fact]
    public async Task An_unset_cap_still_drains_every_new_candidate()
    {
        // 0 is the shipped default, so nothing changes until the key is set.
        var read = await RunAndCountRowsReadAsync(pendingCandidates: 250, batchSize: 100, maxCandidatesPerRun: 0);

        read.Should().Be(250);
    }

    [Fact]
    public async Task The_cap_stops_the_drain_and_bounds_the_rows_read()
    {
        var read = await RunAndCountRowsReadAsync(pendingCandidates: 250, batchSize: 100, maxCandidatesPerRun: 120);

        // Not 250, and not 200 either: the last batch asks for exactly the remaining
        // budget, so the cap is never overshot by up to a full batch.
        read.Should().Be(120);
    }

    [Fact]
    public async Task A_cap_larger_than_the_backlog_changes_nothing()
    {
        var read = await RunAndCountRowsReadAsync(pendingCandidates: 40, batchSize: 100, maxCandidatesPerRun: 5000);

        read.Should().Be(40);
    }

    /// <summary>
    /// Runs one scoring pass against a fake repository holding
    /// <paramref name="pendingCandidates"/> new candidates, and returns how many rows the
    /// process actually read.
    /// </summary>
    private static async Task<int> RunAndCountRowsReadAsync(
        int pendingCandidates,
        int batchSize,
        int maxCandidatesPerRun)
    {
        var remaining = pendingCandidates;
        var rowsRead = 0;

        var mainCandidates = Substitute.For<IMainCandidateRepository>();
        mainCandidates
            .GetNewBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var take = Math.Min(call.Arg<int>(), remaining);
                remaining -= take;
                rowsRead += take;
                return Task.FromResult(Enumerable.Range(0, take).Select(NewCandidate).ToList());
            });
        mainCandidates
            .GetScoredByPlatformAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<MainCandidate>()));

        var session = Substitute.For<IDataSession>();
        session.MainCandidates.Returns(mainCandidates);

        var sessionFactory = Substitute.For<IDataSessionFactory>();
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

        var coverageProvider = Substitute.For<IChampionCoverageProvider>();
        coverageProvider
            .GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ChampionCoverageSnapshot.Empty));

        var process = new ScoringProcess(
            NullLogger<ScoringProcess>.Instance,
            sessionFactory,
            coverageProvider,
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new ScoringOptions
            {
                BatchSize = batchSize,
                MaxCandidatesPerRun = maxCandidatesPerRun,
            }));

        await process.RunCoreAsync(CancellationToken.None);

        return rowsRead;
    }

    private static MainCandidate NewCandidate(int index) => new()
    {
        PlatformId = "KR",
        Puuid = $"puuid-cap-{index}",
        ChampionId = 22,
        ChampionRankInMasteryTop = 1,
        ChampionPoints = 500_000,
        LastPlayTimeUtc = DateTime.UtcNow.AddDays(-1),
        DiscoveredAtUtc = DateTime.UtcNow.AddHours(-1),
        Status = MainCandidateStatus.New,
    };
}
