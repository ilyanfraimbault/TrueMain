using AwesomeAssertions;
using Core.Lol.Identifiers;
using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

/// <summary>
/// What the snapshot pass asks Riot for (#1358): the tracked queue rather than a literal, and a
/// window bounded by the account's last ingest so a claim that comes round again does not re-list
/// — and re-fetch — history it already stored.
/// </summary>
public sealed class MatchSnapshotWriterQueryTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PrepareAsync_AsksForTheTrackedQueue_AndAWindowBoundedByTheLastIngest()
    {
        var lastIngestUtc = NowUtc.AddHours(-3);
        var (writer, matchClient) = BuildWriter();

        await writer.PrepareAsync(
            BuildSession(lastIngestUtc), "KR", "puuid-a", RegionalRoute.Asia, 20, 4, CancellationToken.None);

        var query = matchClient.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IRiotMatchClient.GetMatchIdsAsync))
            .GetArguments()[0]
            .Should().BeOfType<MatchIdQuery>().Subject;

        query.QueueId.Should().Be((int)LolQueueId.RankedSoloDuo, "the queue comes from MainAnalysis:QueueId");
        query.Count.Should().Be(20, "MatchIngestion:MatchesPerAccount stays the authoritative knob");
        query.StartTimeUtc.Should().Be(
            lastIngestUtc.AddHours(-1),
            "a game that ended after the last claim may have started before it");
    }

    [Fact]
    public async Task PrepareAsync_LeavesStartTimeUnset_OnAFirstIngestion()
    {
        var (writer, matchClient) = BuildWriter();

        await writer.PrepareAsync(
            BuildSession(lastIngestUtc: null), "KR", "puuid-a", RegionalRoute.Asia, 20, 4, CancellationToken.None);

        var query = matchClient.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IRiotMatchClient.GetMatchIdsAsync))
            .GetArguments()[0]
            .Should().BeOfType<MatchIdQuery>().Subject;

        query.StartTimeUtc.Should().BeNull("there is no previous claim to bound the window with");
    }

    [Fact]
    public async Task PrepareAsync_WidensTheWindowToTheGamesTheLadderSaysAreOwed()
    {
        var (writer, matchClient) = BuildWriter();

        // 45 games since the last visit against a 20-game window: a fixed window would
        // silently drop 25 of them, and it drops them for the players who play most.
        await writer.PrepareAsync(
            BuildSession(NowUtc.AddDays(-3), ladderGames: 545, ladderGamesAtLastIngest: 500),
            "KR", "puuid-a", RegionalRoute.Asia, 20, 4, CancellationToken.None);

        Query(matchClient).Count.Should().Be(50, "45 owed plus the drift margin");
    }

    [Fact]
    public async Task PrepareAsync_NeverAsksForMoreThanRiotAllows()
    {
        var (writer, matchClient) = BuildWriter();

        await writer.PrepareAsync(
            BuildSession(NowUtc.AddDays(-60), ladderGames: 900, ladderGamesAtLastIngest: 500),
            "KR", "puuid-a", RegionalRoute.Asia, 20, 4, CancellationToken.None);

        Query(matchClient).Count.Should().Be(100, "Riot caps the ids endpoint at 100");
    }

    [Fact]
    public async Task PrepareAsync_KeepsTheConfiguredWindow_WhenTheLadderSaysNothingWasPlayed()
    {
        var (writer, matchClient) = BuildWriter();

        await writer.PrepareAsync(
            BuildSession(NowUtc.AddDays(-3), ladderGames: 500, ladderGamesAtLastIngest: 500),
            "KR", "puuid-a", RegionalRoute.Asia, 20, 4, CancellationToken.None);

        // Never narrower than the configured window: the ladder only covers the swept tiers
        // and only the ranked queue, so "owes nothing" is not proof that nothing was played.
        Query(matchClient).Count.Should().Be(20);
    }

    [Fact]
    public async Task PrepareAsync_KeepsTheConfiguredWindow_WhenTheBaselineIsUnknown()
    {
        var (writer, matchClient) = BuildWriter();

        // An account ingested before the baseline column existed. Reading the missing
        // baseline as zero would ask for the player's entire season.
        await writer.PrepareAsync(
            BuildSession(NowUtc.AddDays(-3), ladderGames: 900, ladderGamesAtLastIngest: null),
            "KR", "puuid-a", RegionalRoute.Asia, 20, 4, CancellationToken.None);

        Query(matchClient).Count.Should().Be(20);
    }

    private static MatchIdQuery Query(IRiotMatchClient matchClient)
        => matchClient.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IRiotMatchClient.GetMatchIdsAsync))
            .GetArguments()[0]
            .Should().BeOfType<MatchIdQuery>().Subject;

    private static (MatchSnapshotWriter Writer, IRiotMatchClient MatchClient) BuildWriter()
    {
        var matchClient = Substitute.For<IRiotMatchClient>();
        matchClient.GetMatchIdsAsync(Arg.Any<MatchIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<string>()));

        var writer = new MatchSnapshotWriter(
            matchClient,
            new FixedTimeProvider(NowUtc),
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions()));

        return (writer, matchClient);
    }

    private static IDataSession BuildSession(
        DateTime? lastIngestUtc,
        int? ladderGames = null,
        int? ladderGamesAtLastIngest = null)
    {
        var accounts = Substitute.For<IRiotAccountRepository>();
        accounts.GetByKeyAsync("KR", "puuid-a", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RiotAccount?>(new RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = "puuid-a",
                PlatformId = "KR",
                LastMatchIngestAtUtc = lastIngestUtc,
                LadderGames = ladderGames,
                LadderGamesAtLastIngest = ladderGamesAtLastIngest
            }));
        accounts.GetByKeysAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Dictionary<AccountKey, RiotAccount>()));

        var matches = Substitute.For<IMatchRepository>();
        matches.GetExistingMatchIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HashSet<string>(StringComparer.Ordinal)));

        var session = Substitute.For<IDataSession>();
        session.RiotAccounts.Returns(accounts);
        session.Matches.Returns(matches);
        return session;
    }
}
