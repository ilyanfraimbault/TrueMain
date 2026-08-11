using AwesomeAssertions;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionDominantLaneFilterTests
{
    [Fact]
    public void KeepDominantLanes_keeps_the_two_most_played_lanes()
    {
        // A flexed champion: mid is the identity, top is a real secondary, the
        // other three are off-role picks that made the directory 5 rows long.
        var rows = new[]
        {
            Lane(position: "MIDDLE", games: 600, lanePlayRate: 0.60),
            Lane(position: "TOP", games: 250, lanePlayRate: 0.25),
            Lane(position: "UTILITY", games: 90, lanePlayRate: 0.09),
            Lane(position: "JUNGLE", games: 40, lanePlayRate: 0.04),
            Lane(position: "BOTTOM", games: 20, lanePlayRate: 0.02),
        };

        var kept = ChampionDominantLaneFilter.KeepDominantLanes(rows, Options());

        kept.Select(row => row.Position).Should().Equal("MIDDLE", "TOP");
    }

    [Fact]
    public void KeepDominantLanes_drops_a_second_lane_below_the_dominance_floor()
    {
        // 96 / 4: the second lane is an off-role pick, not a second identity,
        // so this champion appears once — the cap alone would have kept it.
        var rows = new[]
        {
            Lane(position: "MIDDLE", games: 960, lanePlayRate: 0.96),
            Lane(position: "BOTTOM", games: 40, lanePlayRate: 0.04),
        };

        var kept = ChampionDominantLaneFilter.KeepDominantLanes(rows, Options());

        kept.Select(row => row.Position).Should().Equal("MIDDLE");
    }

    [Fact]
    public void KeepDominantLanes_keeps_a_second_lane_sitting_exactly_on_the_floor()
    {
        // The boundary is the contract of a threshold: a lane holding exactly
        // the configured share is dominant, not "almost".
        var onTheFloor = new[]
        {
            Lane(position: "MIDDLE", games: 900, lanePlayRate: 0.90),
            Lane(position: "TOP", games: 100, lanePlayRate: 0.10),
        };
        var justUnder = new[]
        {
            Lane(position: "MIDDLE", games: 901, lanePlayRate: 0.901),
            Lane(position: "TOP", games: 99, lanePlayRate: 0.099),
        };

        ChampionDominantLaneFilter.KeepDominantLanes(onTheFloor, Options())
            .Select(row => row.Position).Should().Equal("MIDDLE", "TOP");
        ChampionDominantLaneFilter.KeepDominantLanes(justUnder, Options())
            .Select(row => row.Position).Should().Equal("MIDDLE");
    }

    [Fact]
    public void KeepDominantLanes_keeps_the_main_lane_however_thin_its_share()
    {
        // A genuine five-lane flex: no lane clears the 10% floor on its own
        // once the champion is spread evenly. The champion still belongs in a
        // list of champions, so its most-played lane is kept unconditionally.
        var rows = new[]
        {
            Lane(position: "TOP", games: 210, lanePlayRate: 0.21),
            Lane(position: "JUNGLE", games: 200, lanePlayRate: 0.20),
            Lane(position: "MIDDLE", games: 200, lanePlayRate: 0.20),
            Lane(position: "BOTTOM", games: 200, lanePlayRate: 0.20),
            Lane(position: "UTILITY", games: 190, lanePlayRate: 0.19),
        };

        var kept = ChampionDominantLaneFilter.KeepDominantLanes(
            rows, Options(minSecondaryLanePlayRate: 0.25));

        kept.Select(row => row.Position).Should().Equal("TOP");
    }

    [Fact]
    public void KeepDominantLanes_breaks_ties_on_lane_name_so_the_cached_payload_is_stable()
    {
        // Two lanes on identical games: whichever wins, it must win on every
        // request — this payload is cached and served to everyone.
        var rows = new[]
        {
            Lane(position: "MIDDLE", games: 500, lanePlayRate: 0.50),
            Lane(position: "BOTTOM", games: 500, lanePlayRate: 0.50),
        };

        var kept = ChampionDominantLaneFilter.KeepDominantLanes(rows, Options(maxLanesPerChampion: 1));

        // The ordinal tie-break puts BOTTOM ahead of MIDDLE, every time.
        kept.Select(row => row.Position).Should().Equal("BOTTOM");
    }

    [Fact]
    public void KeepDominantLanes_caps_each_champion_independently()
    {
        var rows = new[]
        {
            Lane(championId: 1, position: "MIDDLE", games: 600, lanePlayRate: 0.60),
            Lane(championId: 1, position: "TOP", games: 400, lanePlayRate: 0.40),
            Lane(championId: 2, position: "UTILITY", games: 900, lanePlayRate: 0.90),
            Lane(championId: 1, position: "JUNGLE", games: 100, lanePlayRate: 0.10),
            Lane(championId: 2, position: "BOTTOM", games: 100, lanePlayRate: 0.10),
        };

        var kept = ChampionDominantLaneFilter.KeepDominantLanes(rows, Options());

        // The input order is preserved — only the off-role lines are removed.
        kept.Select(row => (row.ChampionId, row.Position)).Should().Equal(
            [(1, "MIDDLE"), (1, "TOP"), (2, "UTILITY"), (2, "BOTTOM")]);
    }

    [Fact]
    public void KeepDominantLanes_returns_every_row_when_the_cap_is_disabled()
    {
        var rows = new[]
        {
            Lane(position: "MIDDLE", games: 600, lanePlayRate: 0.60),
            Lane(position: "TOP", games: 250, lanePlayRate: 0.25),
            Lane(position: "UTILITY", games: 10, lanePlayRate: 0.01),
        };

        var kept = ChampionDominantLaneFilter.KeepDominantLanes(rows, Options(maxLanesPerChampion: 0));

        kept.Should().HaveCount(3);
    }

    private static ChampionsListOptions Options(
        int maxLanesPerChampion = 2, double minSecondaryLanePlayRate = 0.10) => new()
        {
            MaxLanesPerChampion = maxLanesPerChampion,
            MinSecondaryLanePlayRate = minSecondaryLanePlayRate,
        };

    private static ChampionSummaryReadModel Lane(
        string position, int games, double lanePlayRate, int championId = 1) => new()
        {
            ChampionId = championId,
            Position = position,
            Games = games,
            Wins = games / 2,
            WinRate = 0.5,
            LanePlayRate = lanePlayRate,
            PatchVersion = "16.15",
        };
}
