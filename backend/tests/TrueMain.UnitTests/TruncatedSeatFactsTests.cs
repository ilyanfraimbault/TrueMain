using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Champions.Composition;

namespace TrueMain.UnitTests;

/// <summary>A truncated seat (#2368) folds as the same build facts its live row did.</summary>
public sealed class TruncatedSeatFactsTests
{
    [Fact]
    public void ASeatCarriesItsResolvedBuildFacts()
    {
        var facts = TruncatedSeatFacts.From(Seat([8112, 8139, 8126, 8106, 8226, 8234]), weight: 1.5);

        facts.Win.Should().BeTrue();
        facts.SimilarityWeight.Should().Be(1.5);
        facts.BuildItems.Should().Equal(6655, 4645);
        facts.BootsItemId.Should().Be(3020);
        facts.StarterItems.Should().Equal(1056, 2003);
        (facts.Spell1Id, facts.Spell2Id).Should().Be((4, 14));
        facts.SkillOrderKey.Should().Be("Q-W-E");
        facts.RunePage.Should().Be(new CompositionRunePageFacts(8100, 8112, 8139, 8126, 8106, 8200, 8226, 8234, 5008, 5008, 5001));
    }

    [Fact]
    public void AnIncompleteRunePageIsNoRunePage()
        => TruncatedSeatFacts.From(Seat([])).RunePage.Should().BeNull();

    private static TruncatedMatchSeat Seat(int[] perks) => new()
    {
        MatchId = "EUW1_1",
        ParticipantId = 3,
        Win = true,
        ChampionId = 103,
        TeamPosition = "MIDDLE",
        Summoner1Id = 4,
        Summoner2Id = 14,
        PrimaryStyleId = 8100,
        SubStyleId = 8200,
        RunePerkIds = perks,
        PerksOffense = 5008,
        PerksFlex = 5008,
        PerksDefense = 5001,
        StarterItemIds = [1056, 2003],
        BuildItemIds = [6655, 4645],
        BuildItemMinutes = [14, 22],
        BootsItemId = 3020,
        SkillOrderKey = "Q-W-E",
    };
}
