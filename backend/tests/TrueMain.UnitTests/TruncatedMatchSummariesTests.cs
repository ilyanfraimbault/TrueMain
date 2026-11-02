using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Truemains.Matches;

namespace TrueMain.UnitTests;

/// <summary>A game past match retention as a simplified match row (#2358).</summary>
public sealed class TruncatedMatchSummariesTests
{
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ARowCarriesWhatTheSeatKept_AndNothingItDidNot()
    {
        var self = Seat(1, 100, 103, "MIDDLE", Account);

        var row = TruncatedMatchSummaries.ToSummary(self, [self], new Dictionary<Guid, (string, string?)>());

        row.IsTruncated.Should().BeTrue();
        row.GameMode.Should().Be("CLASSIC");
        row.Self.KeystoneId.Should().Be(8112);
        (row.Self.Kills, row.Self.Deaths, row.Self.Assists).Should().Be((7, 2, 9));
        row.Self.Items.Should().Equal(6655, 4645, 3020);
        row.Self.Win.Should().BeTrue();
        row.Self.Position.Should().Be("MIDDLE");
        (row.Self.Cs, row.Self.PerformanceScore, row.Self.LpDelta).Should().Be((0, 0, null));
    }

    [Fact]
    public void NoBootsAndNoRunePageAddNothing()
    {
        var self = Seat(1, 100, 103, "MIDDLE", null);
        self.BootsItemId = 0;
        self.RunePerkIds = [];

        var row = TruncatedMatchSummaries.ToSummary(self, [self], new Dictionary<Guid, (string, string?)>());

        row.Self.Items.Should().Equal(6655, 4645);
        row.Self.KeystoneId.Should().Be(0);
    }

    [Fact]
    public void TheTeamsAreTheSeatsKept_NamedWhenTheirAccountIsKnown()
    {
        var self = Seat(1, 100, 103, "MIDDLE", Account);
        var anonymous = Seat(6, 200, 238, "", null);

        var row = TruncatedMatchSummaries.ToSummary(
            self,
            [anonymous, self],
            new Dictionary<Guid, (string, string?)> { [Account] = ("Faker", "KR1") });

        row.Participants.Select(participant => participant.ChampionId).Should().Equal(103, 238);
        (row.Participants[0].GameName, row.Participants[0].TagLine).Should().Be(("Faker", "KR1"));
        row.Participants[1].GameName.Should().BeNull();
        row.Participants[1].Position.Should().BeNull();
    }

    private static TruncatedMatchSeat Seat(int participantId, int teamId, int championId, string position, Guid? account) => new()
    {
        MatchId = "EUW1_1",
        ParticipantId = participantId,
        QueueId = 420,
        Patch = "16.20",
        GameStartTimeUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        GameDurationSeconds = 1800,
        TeamId = teamId,
        Win = true,
        ChampionId = championId,
        TeamPosition = position,
        RiotAccountId = account,
        RunePerkIds = [8112, 8139, 8126, 8106, 8226, 8234],
        BuildItemIds = [6655, 4645],
        BuildItemMinutes = [14, 22],
        BootsItemId = 3020,
        Kills = 7,
        Deaths = 2,
        Assists = 9,
    };
}
