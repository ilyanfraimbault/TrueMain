using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Champions.Builds;
using Row = TrueMain.Services.Champions.Builds.ChampionBuildDistributions.ChampionPatternEnrichedRow;

namespace TrueMain.UnitTests;

/// <summary>
/// The pure half of the aggregate build read (#1529): grouping pattern rows into builds and
/// folding each build's per-dimension distributions, without a database.
/// </summary>
public sealed class ChampionBuildDistributionsTests
{
    private static readonly Guid SpellA = Guid.NewGuid();
    private static readonly Guid SpellB = Guid.NewGuid();
    private static readonly Guid Skill = Guid.NewGuid();
    private static readonly Guid Starter = Guid.NewGuid();
    private static readonly Guid Runes = Guid.NewGuid();

    [Fact]
    public void Fold_KeysBuildsByFirstItemAndKeystone_MostPlayedFirst()
    {
        var rows = new[]
        {
            Pattern(SpellA, firstItem: 3078, keystone: 8010, boots: 3047, games: 30, wins: 18),
            Pattern(SpellB, firstItem: 3078, keystone: 8010, boots: 3111, games: 10, wins: 4),
            Pattern(SpellA, firstItem: 6630, keystone: 8010, boots: 3047, games: 50, wins: 25),
            Pattern(SpellA, firstItem: 3078, keystone: 8437, boots: 3047, games: 10, wins: 5),
        };

        var groups = ChampionBuildDistributions.Fold(rows, totalGames: 100);

        groups.Select(g => (g.Key.FirstItemId, g.Key.PrimaryKeystoneId, g.Games))
            .Should().Equal((6630, 8010, 50), (3078, 8010, 40), (3078, 8437, 10));
        var sword = groups[1];
        sword.TopSpells.Select(s => (s.Id, s.Games, s.Wins)).Should().Equal((SpellA, 30, 18), (SpellB, 10, 4));
        sword.TopBoots.Select(b => (b.ItemId, b.Games)).Should().Equal((3047, 30), (3111, 10));
    }

    [Fact]
    public void Fold_DropsBuildsAtOrBelowTheFivePercentPickRateFloor()
    {
        var rows = new[]
        {
            Pattern(SpellA, firstItem: 3078, keystone: 8010, boots: 3047, games: 95, wins: 50),
            Pattern(SpellA, firstItem: 6630, keystone: 8010, boots: 3047, games: 5, wins: 5),
        };

        ChampionBuildDistributions.Fold(rows, totalGames: 100)
            .Select(g => g.Key.FirstItemId)
            .Should().Equal(3078);
    }

    [Fact]
    public void Materialize_SkipsVariationsWhoseDimensionIsMissing()
    {
        var rows = new[]
        {
            Pattern(SpellA, firstItem: 3078, keystone: 8010, boots: 3047, games: 30, wins: 15),
            Pattern(SpellB, firstItem: 3078, keystone: 8010, boots: 3047, games: 10, wins: 5),
        };
        var group = ChampionBuildDistributions.Fold(rows, totalGames: 40).Single();
        var spells = new Dictionary<Guid, ChampionDimSpellPair>
        {
            [SpellB] = new() { Id = SpellB, Spell1Id = 4, Spell2Id = 14 },
        };

        var build = ChampionBuildDistributions.Materialize(group, 40, spells, [], [], []);

        build.PickRate.Should().Be(1);
        build.Core.SummonerSpells!.Spell2Id.Should().Be(14);
        build.Variations.SummonerSpells.Should().ContainSingle();
        build.Core.Boots!.ItemIds.Should().Equal(3047);
        build.Core.SkillOrder.Should().BeNull();
        build.RunePages.Should().BeEmpty();
    }

    private static Row Pattern(Guid spell, int firstItem, int keystone, int boots, int games, int wins)
        => new(spell, Skill, Starter, Runes, firstItem, 3006, 0, 0, 0, 0, 0, boots, keystone, games, wins);
}
