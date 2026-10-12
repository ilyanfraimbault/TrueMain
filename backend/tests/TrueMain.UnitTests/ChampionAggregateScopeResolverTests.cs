using AwesomeAssertions;
using TrueMain.Services.Champions.Builds;
using TrueMain.Services.Champions.Composition;
using TrueMain.Services.Champions.Directory;
using TrueMain.Services.Champions.Mains;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.Progression;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Synergies;

namespace TrueMain.UnitTests;

public sealed class ChampionAggregateScopeResolverTests
{
    [Fact]
    public void ResolveLatestPatchAboveFloor_PicksTheNewestPatchThatClearsTheFloor()
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
    public void ResolveLatestPatchAboveFloor_ReturnsNullWhenNoPatchClearsTheFloor()
    {
        (string GameVersion, string Position, int Games)[] rows =
        [
            ("16.10", "MIDDLE", 3),
            ("16.9", "MIDDLE", 2),
        ];

        ChampionAggregateScopeResolver.ResolveLatestPatchAboveFloor(rows, 5).Should().BeNull();
    }

    [Fact]
    public void ResolveLatestPatchAboveFloor_ComparesTheDominantPositionNotThePatchTotal()
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
    public void ResolveLatestPatchAboveFloor_ExcludesAPatchWithNoValidPosition()
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
    public void ResolveServablePatch_SkipsANewPatchThatCannotFillADirectory()
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
    public void ResolveServablePatch_TakesTheNewestPatchTheMomentItClearsTheBar()
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
    public void ResolveServablePatch_WalksBackPastMoreThanOneThinPatch()
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
    public void ResolveServablePatch_OrdersNumericallyNotLexically()
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
    public void ResolveServablePatch_ServesTheNewestPatchWhenNothingClearsTheBar()
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
    public void ResolveServablePatch_CountsAnUnmeasuredPatchAsZero()
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
    public void ResolveServablePatch_WithTheBarDisabledServesTheNewestPatch(int minLines)
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
    public void ResolveServablePatch_ReturnsNullWhenThereIsNoPatchAtAll()
    {
        ChampionAggregateScopeResolver
            .ResolveServablePatch([], new Dictionary<string, int>(StringComparer.Ordinal), minLines: 50)
            .Should().BeNull();
    }

    [Fact]
    public void OrderNewestFirst_DeduplicatesAndSortsNumerically()
    {
        ChampionAggregateScopeResolver
            .OrderNewestFirst(["16.9", "16.16", "16.9", "17.1"])
            .Should().Equal("17.1", "16.16", "16.9");
    }
}
