using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Ranking;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// #1360: the claim orders by games played since the last visit, so the left-hand side of
/// that subtraction has to track the ladder on every reading — including the ones that
/// change nothing else.
/// </summary>
public sealed class RankSnapshotWriterLadderGamesTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ingest_RecordsTheLadderGameCount()
    {
        var account = NewAccount();

        Write(account, new RankSnapshotInput("DIAMOND", "II", 45, Wins: 120, Losses: 96), latest: null);

        account.LadderGames.Should().Be(216);
    }

    [Fact]
    public void Ingest_KeepsTheCountCurrent_EvenWhenTheRankIsUnchanged()
    {
        var account = NewAccount();
        var latest = new RankSnapshot
        {
            Id = Guid.NewGuid(),
            RiotAccountId = account.Id,
            CapturedAtUtc = Now.AddDays(-1),
            Tier = "DIAMOND",
            Division = "II",
            LeaguePoints = 45,
            Wins = 120,
            Losses = 96
        };

        // A win and a loss return to the same LP: nothing about the rank moved, but two
        // games were played, and those are exactly the games the claim must not miss.
        var outcome = Write(account, new RankSnapshotInput("DIAMOND", "II", 45, Wins: 121, Losses: 97), latest);

        outcome.Should().Be(RankSnapshotOutcome.Unchanged);
        account.LadderGames.Should().Be(218);
    }

    [Fact]
    public void Ingest_LeavesTheCountAlone_WhenTheReadingCarriesNoWinsOrLosses()
    {
        var account = NewAccount();
        account.LadderGames = 216;

        Write(account, new RankSnapshotInput("DIAMOND", "II", 50, Wins: null, Losses: null), latest: null);

        // An apex ladder entry without a record must not read as "zero games played",
        // which would make the account look permanently up to date.
        account.LadderGames.Should().Be(216);
    }

    [Fact]
    public void Ingest_DoesNotTouchTheIngestBaseline()
    {
        var account = NewAccount();
        account.LadderGames = 500;
        account.LadderGamesAtLastIngest = 500;

        Write(account, new RankSnapshotInput("DIAMOND", "I", 60, Wins: 300, Losses: 260), latest: null);

        // Only a completed ingestion moves the baseline; a rank reading moves the other side
        // of the subtraction, which is what makes the difference mean "owed".
        account.LadderGames.Should().Be(560);
        account.LadderGamesAtLastIngest.Should().Be(500);
    }

    private static RankSnapshotOutcome Write(RiotAccount account, RankSnapshotInput input, RankSnapshot? latest)
    {
        var session = Substitute.For<IDataSession>();
        session.RankSnapshots.Returns(Substitute.For<IRankSnapshotRepository>());
        return new RankSnapshotWriter().Ingest(session, account, input, latest, Now);
    }

    private static RiotAccount NewAccount() => new()
    {
        Id = Guid.NewGuid(),
        Puuid = "puuid",
        PlatformId = "KR",
        GameName = "player",
        TagLine = "KR1"
    };
}
