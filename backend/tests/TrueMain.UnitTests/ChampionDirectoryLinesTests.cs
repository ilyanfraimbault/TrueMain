using AwesomeAssertions;
using Data.Aggregation;

namespace TrueMain.UnitTests;

/// <summary>
/// The definition three consumers share (#1109) — the public directory, the admin
/// patch-coverage page and the servable-patch bar. These tests are the contract
/// between them, not an implementation detail of any one.
/// </summary>
public sealed class ChampionDirectoryLinesTests
{
    [Fact]
    public void Fold_sums_the_scope_rows_behind_one_champion_lane_line()
    {
        // A scope row is per (account, champion, patch, platform, queue, lane, elo),
        // so a line is many rows: counting rows instead of folding them would report a
        // patch as rankable off a handful of games spread thin.
        ChampionDirectoryLine[] rows =
        [
            new("16.16", 1, "TOP", 4),
            new("16.16", 1, "TOP", 6),
            new("16.16", 1, "MIDDLE", 2),
        ];

        ChampionDirectoryLines.Fold(rows).Should().BeEquivalentTo(new[]
        {
            new ChampionDirectoryLine("16.16", 1, "TOP", 10),
            new ChampionDirectoryLine("16.16", 1, "MIDDLE", 2),
        });
    }

    [Fact]
    public void Fold_keeps_patches_apart()
    {
        ChampionDirectoryLine[] rows =
        [
            new("16.16", 1, "TOP", 4),
            new("16.15", 1, "TOP", 600),
        ];

        ChampionDirectoryLines.Fold(rows).Should().BeEquivalentTo(new[]
        {
            new ChampionDirectoryLine("16.16", 1, "TOP", 4),
            new ChampionDirectoryLine("16.15", 1, "TOP", 600),
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Fold_drops_the_lane_less_sentinel(string position)
    {
        // Position is non-nullable, so "no lane" is a blank string. Such a row reaches
        // no public page, so counting it as coverage would let a patch clear the bar on
        // lines the directory will never print.
        ChampionDirectoryLine[] rows =
        [
            new("16.16", 1, position, 900),
            new("16.16", 2, "TOP", 12),
        ];

        ChampionDirectoryLines.Fold(rows).Should().ContainSingle()
            .Which.ChampionId.Should().Be(2);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void ClearsFloor_is_inclusive(long games, bool expected)
    {
        ChampionDirectoryLines
            .ClearsFloor(new ChampionDirectoryLine("16.16", 1, "TOP", games), floor: 10)
            .Should().Be(expected);
    }

    [Fact]
    public void ClearsFloor_with_no_floor_keeps_a_single_game_line()
    {
        ChampionDirectoryLines
            .ClearsFloor(new ChampionDirectoryLine("16.16", 1, "TOP", 1), floor: 0)
            .Should().BeTrue("a floor of 0 is the documented off-switch");
    }

    [Fact]
    public void BelowFloorOnPrimaryLane_keeps_the_champions_own_lane_and_drops_the_off_role_tail()
    {
        // Champion 1 is a MIDDLE played twice on UTILITY, champion 2 a TOP that is simply
        // short of games. Naming the UTILITY line would send an operator chasing games that
        // are not coming: it is short because nobody plays the champion there.
        ChampionDirectoryLine[] lines =
        [
            new("16.16", 1, "MIDDLE", 40),
            new("16.16", 1, "UTILITY", 2),
            new("16.16", 2, "TOP", 7),
        ];

        ChampionDirectoryLines.BelowFloorOnPrimaryLane(lines, floor: 10)
            .Should().ContainSingle()
            .Which.Should().Be(new ChampionDirectoryLine("16.16", 2, "TOP", 7));
    }

    [Fact]
    public void BelowFloorOnPrimaryLane_keeps_a_champion_whose_only_lane_is_short()
    {
        // One lane makes that lane the champion's own by construction — a champion nobody
        // has enough games on anywhere is exactly the gap the list is for.
        ChampionDirectoryLine[] lines = [new("16.16", 1, "JUNGLE", 3)];

        ChampionDirectoryLines.BelowFloorOnPrimaryLane(lines, floor: 10)
            .Should().ContainSingle()
            .Which.Position.Should().Be("JUNGLE");
    }

    [Fact]
    public void BelowFloorOnPrimaryLane_orders_the_lines_closest_to_the_floor_first()
    {
        // The question a thin patch raises is "how far off is it", so the lines about to
        // clear lead. Equal games break on champion then lane, so the order is stable.
        ChampionDirectoryLine[] lines =
        [
            new("16.16", 1, "TOP", 2),
            new("16.16", 2, "MIDDLE", 9),
            new("16.16", 3, "BOTTOM", 5),
        ];

        ChampionDirectoryLines.BelowFloorOnPrimaryLane(lines, floor: 10)
            .Select(line => line.Games)
            .Should().ContainInOrder(9, 5, 2);
    }
}
