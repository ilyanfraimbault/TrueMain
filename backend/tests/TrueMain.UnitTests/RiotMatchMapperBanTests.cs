using AwesomeAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers the <c>teams[].bans[]</c> arm of the mapper (#920). Two shapes have to
/// be dropped before the rows reach EF: Riot's <c>-1</c> sentinel for a ban slot
/// nobody used, and a duplicated <c>(teamId, pickTurn)</c>, which is the natural
/// primary key of <c>match_bans</c> and would fail the whole ingestion batch's
/// save rather than just its own row.
/// </summary>
public sealed class RiotMatchMapperBanTests
{
    private const string TestMatchId = "KR_1234567890";

    [Fact]
    public void MapBans_FlattensBothTeamsInPayloadOrder()
    {
        var match = BuildMatchWithBans(
            (100, [(266, 1), (103, 2)]),
            (200, [(84, 3), (12, 4)]));

        var bans = RiotMatchMapper.MapBans(match, TestMatchId);

        bans.Select(ban => (ban.TeamId, ban.PickTurn, ban.ChampionId))
            .Should().Equal((100, 1, 266), (100, 2, 103), (200, 3, 84), (200, 4, 12));
        bans.Should().AllSatisfy(ban => ban.MatchId.Should().Be(TestMatchId));
    }

    [Fact]
    public void MapBans_DropsUnusedBanSlots()
    {
        // -1 is what Riot sends when a player let the ban timer run out. Storing it
        // would invent a champion id 0 or -1 in the ban aggregates.
        var match = BuildMatchWithBans(
            (100, [(266, 1), (-1, 2)]),
            (200, [(-1, 3)]));

        var bans = RiotMatchMapper.MapBans(match, TestMatchId);

        bans.Should().ContainSingle();
        bans[0].ChampionId.Should().Be(266);
    }

    [Fact]
    public void MapBans_KeepsTheFirstOfADuplicatedPickTurn()
    {
        // Cannot happen in a well-formed draft, but the row is the table's PK: a
        // duplicate that reached SaveChanges would fail the batch, not the row.
        var match = BuildMatchWithBans((100, [(266, 1), (103, 1)]));

        var bans = RiotMatchMapper.MapBans(match, TestMatchId);

        bans.Should().ContainSingle();
        bans[0].ChampionId.Should().Be(266);
    }

    [Fact]
    public void MapBans_AllowsTheSameChampionOnBothTeams()
    {
        // Distinct pick turns, so both rows are legal. The aggregation is what
        // collapses them to one ban per match; the mapper must not pre-empt it.
        var match = BuildMatchWithBans(
            (100, [(266, 1)]),
            (200, [(266, 2)]));

        var bans = RiotMatchMapper.MapBans(match, TestMatchId);

        bans.Should().HaveCount(2);
        bans.Should().AllSatisfy(ban => ban.ChampionId.Should().Be(266));
    }

    [Fact]
    public void MapBans_ReturnsEmpty_WhenThePayloadCarriesNoTeams()
    {
        // Every match ingested before #920 looked like this to the mapper, and any
        // queue without a draft still does.
        var bans = RiotMatchMapper.MapBans(BuildMatchWithBans(), TestMatchId);

        bans.Should().BeEmpty();
    }

    private static RiotMatchDto BuildMatchWithBans(
        params (int TeamId, (int ChampionId, int PickTurn)[] Bans)[] teams)
        => new()
        {
            Metadata = new RiotMatchMetadataDto { MatchId = TestMatchId },
            Info = new RiotMatchInfoDto
            {
                QueueId = 420,
                GameVersion = "16.4.1",
                Participants = [],
                Teams = [.. teams.Select(team => new RiotTeamDto
                {
                    TeamId = team.TeamId,
                    Bans = [.. team.Bans.Select(ban => new RiotBanDto
                    {
                        ChampionId = ban.ChampionId,
                        PickTurn = ban.PickTurn
                    })]
                })]
            }
        };
}
