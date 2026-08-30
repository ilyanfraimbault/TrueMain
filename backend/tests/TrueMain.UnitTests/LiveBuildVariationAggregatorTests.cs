using AwesomeAssertions;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

/// <summary>
/// The matchup fold (#923). The claim that matters is the one the issue was reopened
/// over: the core build is not a separate computation that survives filtering — it is
/// the most common value of each dimension <em>inside the slice</em>, so a
/// matchup-filtered slice must move it.
/// </summary>
public sealed class LiveBuildVariationAggregatorTests
{
    [Fact]
    public void Aggregate_DerivesTheCoreFromTheSliceItIsGiven()
    {
        // Two slices differing only in which build dominates. If the core were computed
        // from anything but the slice, both would answer the same thing.
        var swordSlice = Facts(
            ([1001, 3006], true, 8010),
            ([1001, 3006], false, 8010),
            ([2001, 3020], true, 8010));
        var staffSlice = Facts(
            ([2001, 3020], true, 8010),
            ([2001, 3020], false, 8010),
            ([1001, 3006], true, 8010));

        var swordCore = LiveBuildVariationAggregator.Aggregate(swordSlice)[0].Core;
        var staffCore = LiveBuildVariationAggregator.Aggregate(staffSlice)[0].Core;

        swordCore.ItemPath!.ItemIds.Should().Equal(1001, 3006);
        staffCore.ItemPath!.ItemIds.Should().Equal(2001, 3020);
    }

    [Fact]
    public void Aggregate_MatchesTheCoreToTheTopVariation()
    {
        // The core is rendered above the variations it is drawn from; if it named a
        // build that is not the first of that list, the page would contradict itself.
        var builds = LiveBuildVariationAggregator.Aggregate(Facts(
            ([1001, 3006], true, 8010),
            ([1001, 3006], true, 8010),
            ([1001, 3006], false, 8010)));

        var build = builds.Single();
        build.Core.Boots.Should().BeEquivalentTo(build.Variations.Boots[0]);
        build.Core.RunePage.Should().BeEquivalentTo(build.RunePages[0]);
        build.Core.SkillOrder.Should().BeEquivalentTo(build.Variations.SkillOrder[0]);
    }

    [Fact]
    public void Aggregate_CarriesTheGameCountOnEveryVariation()
    {
        // The 2026-07-30 decision — show a thin matchup rather than hide it — only holds
        // if the volume travels with the numbers. A variation without its game count is a
        // 100% winrate with nothing to qualify it.
        var builds = LiveBuildVariationAggregator.Aggregate(Facts(
            ([1001, 3006], true, 8010),
            ([1001, 3006], false, 8010)));

        var build = builds.Single();
        build.Games.Should().Be(2);
        build.Variations.Boots.Should().OnlyContain(variation => variation.Games > 0);
        build.RunePages.Should().OnlyContain(page => page.Games > 0);
    }

    [Fact]
    public void Aggregate_ReportsWinRatesAgainstTheirOwnGames_NotTheSlice()
    {
        // One game at 100% must read as 1 game at 100%, not as a share of the slice.
        var builds = LiveBuildVariationAggregator.Aggregate(Facts(
            ([1001, 3006], true, 8010),
            ([1001, 3006], false, 8010),
            ([1001, 3006], false, 8010)));

        var build = builds.Single();
        build.WinRate.Should().BeApproximately(1d / 3, 0.001);
        build.PickRate.Should().Be(1d, "every game in the slice is on this build");
    }

    [Fact]
    public void Aggregate_SplitsBuildsByFirstItemAndKeystone()
    {
        var builds = LiveBuildVariationAggregator.Aggregate(Facts(
            ([1001, 3006], true, 8010),
            ([1001, 3006], true, 8010),
            ([2001, 3020], false, 8021)));

        builds.Should().HaveCount(2);
        builds[0].FirstItemId.Should().Be(1001, "the most played build leads");
        builds[0].PrimaryKeystoneId.Should().Be(8010);
        builds[0].PickRate.Should().BeApproximately(2d / 3, 0.001);
    }

    [Fact]
    public void Aggregate_ReturnsNothing_ForAnEmptySlice()
    {
        LiveBuildVariationAggregator.Aggregate([]).Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_CapsBuildsAndVariations_LikeTheAggregatePath()
    {
        // Both paths feed the same Vue panel, so applying a ?vs= filter must not
        // change how many tabs or variations it renders (#1240). Six distinct
        // builds, each with its own skill order, must come back as
        // ChampionBuildDisplayCaps.MaxBuilds tabs; the busiest build's skill
        // orders capped at MaxVariations.
        List<CompositionParticipantFacts> facts = Facts(
            ([1001, 3006], true, 8010),
            ([1002, 3006], true, 8010),
            ([1003, 3006], true, 8010),
            ([1004, 3006], true, 8010),
            ([1005, 3006], true, 8010),
            ([1006, 3006], true, 8010));

        // Give the leading build five games with five distinct skill orders.
        for (int i = 0; i < 4; i++)
        {
            facts.Add(new CompositionParticipantFacts
            {
                Win = true,
                BuildItems = [1001, 3006],
                BootsItemId = 3006,
                StarterItems = [1055, 2003],
                Spell1Id = 4,
                Spell2Id = 14,
                SkillOrderKey = $"Q-W-E-{i}",
                RunePage = new CompositionRunePageFacts(
                    8000, 8010, 8009, 9111, 9104, 8100, 8139, 8135, 5008, 5008, 5001),
            });
        }

        IReadOnlyList<ChampionBuildReadModel> builds = LiveBuildVariationAggregator.Aggregate(facts);

        builds.Should().HaveCount(ChampionBuildDisplayCaps.MaxBuilds);
        builds[0].Variations.SkillOrder.Should().HaveCount(ChampionBuildDisplayCaps.MaxVariations);
    }

    [Fact]
    public void Aggregate_IgnoresGamesItCannotPlace_WithoutInflatingTheOthers()
    {
        // A game with no resolvable build (missing item metadata for its patch) cannot be
        // put on the first-item × keystone grid, but it was still a game of this matchup:
        // the pick rates stay against the whole slice so they cannot add up to more than
        // the matchup actually covers.
        var facts = Facts(
            ([1001, 3006], true, 8010),
            ([1001, 3006], true, 8010));
        facts.Add(new CompositionParticipantFacts { Win = true, SkillOrderKey = "Q-W-E" });

        var build = LiveBuildVariationAggregator.Aggregate(facts).Single();

        build.Games.Should().Be(2);
        build.PickRate.Should().BeApproximately(2d / 3, 0.001);
    }

    private static List<CompositionParticipantFacts> Facts(
        params (int[] Items, bool Win, int Keystone)[] rows)
        => [.. rows.Select(row => new CompositionParticipantFacts
        {
            Win = row.Win,
            BuildItems = row.Items,
            BootsItemId = 3006,
            StarterItems = [1055, 2003],
            Spell1Id = 4,
            Spell2Id = 14,
            SkillOrderKey = "Q-W-E-Q",
            RunePage = new CompositionRunePageFacts(
                8000, row.Keystone, 8009, 9111, 9104, 8100, 8139, 8135, 5008, 5008, 5001),
        })];
}
