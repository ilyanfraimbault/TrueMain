using AwesomeAssertions;
using Core.Lol.Map;
using Core.Lol.Performance;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// The glue that turns stored timeline rows into the two timeline-derived
/// inputs of <see cref="PerformanceScore"/>. Shared by every scoring surface,
/// so its "missing means dropped, not zero" contract is pinned here once.
/// </summary>
public sealed class PerformanceInputsTests
{
    private const int BlueTeamId = 100;
    private const int RedTeamId = 200;

    private static Dictionary<(int ParticipantId, int Minute), TimelineMark> Marks(
        params TimelineMark[] marks)
        => marks.ToDictionary(m => (m.ParticipantId, m.Minute));

    [Fact]
    public void BuildLaneLeads_emits_one_entry_per_mark_both_sides_cover()
    {
        var marks = Marks(
            new TimelineMark(1, 10, Cs: 80, Gold: 4_000, Xp: 5_000),
            new TimelineMark(6, 10, Cs: 70, Gold: 3_500, Xp: 4_800),
            new TimelineMark(1, 15, Cs: 130, Gold: 6_500, Xp: 8_000),
            new TimelineMark(6, 15, Cs: 115, Gold: 5_750, Xp: 7_250),
            // Minute 20 exists for the player only — no comparison is possible.
            new TimelineMark(1, 20, Cs: 180, Gold: 9_000, Xp: 11_000));

        var leads = PerformanceInputs.BuildLaneLeads(1, 6, marks);

        leads.Should().Equal(
            new LaneLead(10, GoldDiff: 500, CsDiff: 10, XpDiff: 200),
            new LaneLead(15, GoldDiff: 750, CsDiff: 15, XpDiff: 750));
    }

    [Fact]
    public void BuildLaneLeads_returns_nothing_without_an_opponent()
    {
        var marks = Marks(new TimelineMark(1, 15, 130, 6_500, 8_000));

        PerformanceInputs.BuildLaneLeads(1, opponentParticipantId: null, marks)
            .Should().BeEmpty();
    }

    [Fact]
    public void BuildLaneLeads_returns_the_marks_in_ascending_minute_order()
    {
        // Dictionary iteration order is not the wire order; the builder walks the
        // canonical mark list instead, so the curve is always sorted.
        var marks = Marks(
            new TimelineMark(1, 30, 300, 15_000, 19_000),
            new TimelineMark(6, 30, 280, 14_000, 18_000),
            new TimelineMark(1, 5, 35, 1_800, 2_200),
            new TimelineMark(6, 5, 30, 1_700, 2_100));

        PerformanceInputs.BuildLaneLeads(1, 6, marks)
            .Select(l => l.Minute)
            .Should().Equal(5, 30);
    }

    [Fact]
    public void FindLaneOpponent_pairs_the_single_enemy_on_the_same_position()
    {
        var roster = Roster(("MIDDLE", 100), ("TOP", 100), ("MIDDLE", 200), ("TOP", 200));

        PerformanceInputs.FindLaneOpponent(roster, roster[0]).Should().Be(3);
        PerformanceInputs.FindLaneOpponent(roster, roster[2]).Should().Be(1);
    }

    [Fact]
    public void FindLaneOpponent_refuses_an_ambiguous_pairing()
    {
        // Anomalous data: two enemies on one position. Taking the first would
        // make the score depend on Postgres' row order, so there is no opponent
        // and the lead components drop instead.
        var roster = Roster(("MIDDLE", 100), ("MIDDLE", 200), ("MIDDLE", 200));

        PerformanceInputs.FindLaneOpponent(roster, roster[0]).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FindLaneOpponent_has_nothing_to_pair_an_unassigned_position_with(string? position)
    {
        var roster = Roster((position, 100), (position, 200));

        PerformanceInputs.FindLaneOpponent(roster, roster[0]).Should().BeNull();
    }

    [Fact]
    public void BuildMatchInputs_drops_the_lead_components_on_an_ambiguous_pairing()
    {
        // Same rosters, same snapshots — but the second has a duplicate enemy
        // MIDDLE, so the player has no opponent and grades as if there were no
        // timeline at all.
        var marks = Marks(
            new TimelineMark(1, 15, Cs: 130, Gold: 6_500, Xp: 8_000),
            new TimelineMark(2, 15, Cs: 115, Gold: 5_750, Xp: 7_250),
            new TimelineMark(3, 15, Cs: 100, Gold: 5_000, Xp: 6_500));

        var unambiguous = Roster(("MIDDLE", 100), ("MIDDLE", 200));
        var ambiguous = Roster(("MIDDLE", 100), ("MIDDLE", 200), ("MIDDLE", 200));

        LeadsOf(unambiguous, marks).Should().NotBeEmpty();
        LeadsOf(ambiguous, marks).Should().BeEmpty();
    }

    private static IReadOnlyList<LaneLead> LeadsOf(
        IReadOnlyList<ScoredParticipant> roster,
        Dictionary<(int ParticipantId, int Minute), TimelineMark> marks)
        => PerformanceInputs
            .BuildMatchInputs(roster, durationSeconds: 1_800, marks, Array.Empty<KillSpot>())
            .First(b => b.Participant.ParticipantId == 1)
            .Input
            .LaneLeads;

    /// <summary>
    /// Builds a roster from (position, teamId) pairs; participant ids are 1-based
    /// in declaration order and every stat line is identical, so only the pairing
    /// varies between cases.
    /// </summary>
    private static IReadOnlyList<ScoredParticipant> Roster(params (string? Position, int TeamId)[] slots)
        => slots
            .Select((slot, index) => new ScoredParticipant(
                ParticipantId: index + 1,
                TeamId: slot.TeamId,
                TeamPosition: slot.Position,
                Win: slot.TeamId == BlueTeamId,
                Kills: 5,
                Deaths: 4,
                Assists: 6,
                Cs: 180,
                DamageToChampions: 18_000,
                GoldEarned: 12_000,
                VisionScore: 20))
            .ToList();

    [Fact]
    public void CountOutOfLaneTakedowns_is_unknown_when_the_match_has_no_coverage()
    {
        PerformanceInputs.CountOutOfLaneTakedowns(
                1, "MIDDLE", BlueTeamId, matchHasKillPositions: false, Array.Empty<KillSpot>())
            .Should().BeNull();
    }

    [Fact]
    public void CountOutOfLaneTakedowns_is_zero_for_a_covered_match_with_no_roam()
    {
        // A covered match in which this player never left their lane is a real 0
        // and must be graded as one — the distinction the score depends on.
        var midOfMid = MidLaneSpot(1);

        PerformanceInputs.CountOutOfLaneTakedowns(
                1, "MIDDLE", BlueTeamId, matchHasKillPositions: true, new[] { midOfMid })
            .Should().Be(0);
    }

    [Fact]
    public void CountOutOfLaneTakedowns_counts_only_this_participants_out_of_lane_kills()
    {
        var botLaneSpot = BotLaneSpot(1);
        var otherPlayersBotKill = BotLaneSpot(4);

        PerformanceInputs.CountOutOfLaneTakedowns(
                1,
                "MIDDLE",
                BlueTeamId,
                matchHasKillPositions: true,
                new[] { MidLaneSpot(1), botLaneSpot, otherPlayersBotKill })
            .Should().Be(1);
    }

    [Fact]
    public void CountOutOfLaneTakedowns_is_unknown_for_a_jungler()
    {
        // A jungler has no own lane, so the classification is meaningless rather
        // than "everything is a roam".
        PerformanceInputs.CountOutOfLaneTakedowns(
                1, "JUNGLE", BlueTeamId, matchHasKillPositions: true, new[] { BotLaneSpot(1) })
            .Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("NONE")]
    public void CountOutOfLaneTakedowns_is_unknown_for_an_unparsed_position(string position)
        => PerformanceInputs.CountOutOfLaneTakedowns(
                1, position, RedTeamId, matchHasKillPositions: true, new[] { BotLaneSpot(1) })
            .Should().BeNull();

    [Theory]
    [InlineData("top", MapZone.TopLane)]
    [InlineData("  MIDDLE ", MapZone.MidLane)]
    [InlineData("BOTTOM", MapZone.BotLane)]
    [InlineData("UTILITY", MapZone.BotLane)]
    [InlineData("JUNGLE", MapZone.Unknown)]
    [InlineData(null, MapZone.Unknown)]
    public void OwnLane_maps_a_team_position_to_its_home_lane(string? position, MapZone expected)
        => PerformanceInputs.OwnLane(position).Should().Be(expected);

    /// <summary>A point on the mid-lane diagonal, well away from either base.</summary>
    private static KillSpot MidLaneSpot(int participantId) => new(participantId, 7_400, 7_400);

    /// <summary>A point deep in the bot lane, on the flat stretch red side of the river.</summary>
    private static KillSpot BotLaneSpot(int participantId) => new(participantId, 11_000, 1_100);
}
