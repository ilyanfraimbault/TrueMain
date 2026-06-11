using AwesomeAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionAggregateScopeResolverTests
{
    [Fact]
    public void ResolveLatestPatchAboveFloor_picks_the_newest_patch_that_clears_the_floor()
    {
        (string GameVersion, string Position, int Games)[] rows =
        [
            ("16.10", "MIDDLE", 3), // latest, but below the floor
            ("16.9", "MIDDLE", 6),  // newest patch that clears 5
            ("16.8", "MIDDLE", 9),
        ];

        ChampionAggregateScopeResolver.ResolveLatestPatchAboveFloor(rows, 5).Should().Be("16.9");
    }

    [Fact]
    public void ResolveLatestPatchAboveFloor_returns_null_when_no_patch_clears_the_floor()
    {
        (string GameVersion, string Position, int Games)[] rows =
        [
            ("16.10", "MIDDLE", 3),
            ("16.9", "MIDDLE", 2),
        ];

        ChampionAggregateScopeResolver.ResolveLatestPatchAboveFloor(rows, 5).Should().BeNull();
    }

    [Fact]
    public void ResolveLatestPatchAboveFloor_compares_the_dominant_position_not_the_patch_total()
    {
        // 16.9 spreads 3+3 across two roles — the patch total is 6 but neither
        // role clears 5, so the resolver skips it for 16.8's single 6-game role.
        (string GameVersion, string Position, int Games)[] rows =
        [
            ("16.9", "MIDDLE", 3),
            ("16.9", "TOP", 3),
            ("16.8", "MIDDLE", 6),
        ];

        ChampionAggregateScopeResolver.ResolveLatestPatchAboveFloor(rows, 5).Should().Be("16.8");
    }

    [Fact]
    public void ResolveLatestPatchAboveFloor_excludes_a_patch_with_no_valid_position()
    {
        // Defensive branch: a patch whose only rows have a blank position can't
        // form a rankable slice, so it's skipped for the newest patch that can.
        (string GameVersion, string Position, int Games)[] rows =
        [
            ("16.9", "", 10),
            ("16.8", "MIDDLE", 6),
        ];

        ChampionAggregateScopeResolver.ResolveLatestPatchAboveFloor(rows, 5).Should().Be("16.8");
    }
}
