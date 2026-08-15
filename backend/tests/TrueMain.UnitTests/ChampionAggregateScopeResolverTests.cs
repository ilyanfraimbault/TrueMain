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

    [Fact]
    public void ResolveServablePatch_skips_a_new_patch_that_cannot_fill_a_directory()
    {
        // The #1109 regression, in miniature: 16.16 exists and holds rows, but only
        // seven of its lines clear the min-sample floor. Serving it would put an empty
        // directory and an empty tier list on screen while 16.15 sits beside it.
        var linesPastFloor = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["16.16"] = 7,
            ["16.15"] = 561,
            ["16.14"] = 540,
        };

        ChampionAggregateScopeResolver
            .ResolveServablePatch(linesPastFloor.Keys, linesPastFloor, minLines: 50)
            .Should().Be("16.15");
    }

    [Fact]
    public void ResolveServablePatch_takes_the_newest_patch_the_moment_it_clears_the_bar()
    {
        var linesPastFloor = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["16.16"] = 50, // exactly at the bar — the bar is inclusive
            ["16.15"] = 561,
        };

        ChampionAggregateScopeResolver
            .ResolveServablePatch(linesPastFloor.Keys, linesPastFloor, minLines: 50)
            .Should().Be("16.16");
    }

    [Fact]
    public void ResolveServablePatch_walks_back_past_more_than_one_thin_patch()
    {
        // Two thin patches in a row is not a shape production has produced, but the
        // walk must not stop after a single step or the fallback silently lands on
        // another empty directory.
        var linesPastFloor = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["16.16"] = 0,
            ["16.15"] = 12,
            ["16.14"] = 540,
        };

        ChampionAggregateScopeResolver
            .ResolveServablePatch(linesPastFloor.Keys, linesPastFloor, minLines: 50)
            .Should().Be("16.14");
    }

    [Fact]
    public void ResolveServablePatch_orders_numerically_not_lexically()
    {
        // "16.9" sorts after "16.16" as text. A lexical walk would serve the older
        // patch forever once the minor number passed 9.
        var linesPastFloor = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["16.9"] = 540,
            ["16.16"] = 540,
        };

        ChampionAggregateScopeResolver
            .ResolveServablePatch(linesPastFloor.Keys, linesPastFloor, minLines: 50)
            .Should().Be("16.16");
    }

    [Fact]
    public void ResolveServablePatch_serves_the_newest_patch_when_nothing_clears_the_bar()
    {
        // A fresh deployment, or a bar set above the whole site's volume: a thin
        // directory is the honest state, an empty one is not.
        var linesPastFloor = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["16.16"] = 3,
            ["16.15"] = 4,
        };

        ChampionAggregateScopeResolver
            .ResolveServablePatch(linesPastFloor.Keys, linesPastFloor, minLines: 50)
            .Should().Be("16.16");
    }

    [Fact]
    public void ResolveServablePatch_counts_an_unmeasured_patch_as_zero()
    {
        // A candidate missing from the lookup has no measured lines, so it can never
        // win the walk — the alternative (treating "unknown" as "fine") is exactly the
        // pre-#1109 behaviour.
        var linesPastFloor = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["16.15"] = 561,
        };

        ChampionAggregateScopeResolver
            .ResolveServablePatch(["16.16", "16.15"], linesPastFloor, minLines: 50)
            .Should().Be("16.15");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ResolveServablePatch_with_the_bar_disabled_serves_the_newest_patch(int minLines)
    {
        var linesPastFloor = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["16.16"] = 1,
            ["16.15"] = 561,
        };

        ChampionAggregateScopeResolver
            .ResolveServablePatch(linesPastFloor.Keys, linesPastFloor, minLines)
            .Should().Be("16.16", "0 is the documented off-switch, back to the pre-#1109 rule");
    }

    [Fact]
    public void ResolveServablePatch_returns_null_when_there_is_no_patch_at_all()
    {
        ChampionAggregateScopeResolver
            .ResolveServablePatch([], new Dictionary<string, int>(StringComparer.Ordinal), minLines: 50)
            .Should().BeNull();
    }

    [Fact]
    public void OrderNewestFirst_deduplicates_and_sorts_numerically()
    {
        ChampionAggregateScopeResolver
            .OrderNewestFirst(["16.9", "16.16", "16.9", "17.1"])
            .Should().Equal("17.1", "16.16", "16.9");
    }
}
