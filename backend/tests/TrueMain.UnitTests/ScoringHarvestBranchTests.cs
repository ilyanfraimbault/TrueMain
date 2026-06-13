using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class ScoringHarvestBranchTests
{
    [Fact]
    public async Task HarvestCandidate_ScoresFromObservedGames_WithoutMasteryData()
    {
        // No mastery rank/points at all — the harvest branch must still yield a positive
        // score from observed games + recency (the ladder rank formula would read rank 0).
        var score = await ScoreAsync(observedGames: 20, lastPlayDaysAgo: 1);

        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HarvestCandidate_WithMoreObservedGames_ScoresHigher()
    {
        var fewer = await ScoreAsync(observedGames: 5, lastPlayDaysAgo: 1);
        var more = await ScoreAsync(observedGames: 40, lastPlayDaysAgo: 1);

        more.Should().BeGreaterThan(fewer);
    }

    private static async Task<double> ScoreAsync(int observedGames, int lastPlayDaysAgo)
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        var session = Substitute.For<IDataSession>();
        var mainCandidates = Substitute.For<IMainCandidateRepository>();
        var candidate = new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-harvest-1",
            ChampionId = 22,
            Source = MainCandidateSource.Harvest,
            ObservedGames = observedGames,
            ObservedWins = observedGames / 2,
            LastPlayTimeUtc = DateTime.UtcNow.AddDays(-lastPlayDaysAgo),
            DiscoveredAtUtc = DateTime.UtcNow.AddHours(-1),
            Status = MainCandidateStatus.New
        };

        mainCandidates.GetNewBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new List<MainCandidate> { candidate }),
                Task.FromResult(new List<MainCandidate>()));
        mainCandidates.GetScoredByPlatformAsync("KR", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<MainCandidate> { candidate }));

        session.MainCandidates.Returns(mainCandidates);
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

        var coverageProvider = Substitute.For<IChampionCoverageProvider>();
        coverageProvider.GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ChampionCoverageSnapshot.Empty));

        var process = new ScoringProcess(
            NullLogger<ScoringProcess>.Instance,
            sessionFactory,
            coverageProvider,
            Microsoft.Extensions.Options.Options.Create(new ScoringOptions()));

        await process.RunCoreAsync(CancellationToken.None);

        return candidate.Score;
    }
}
