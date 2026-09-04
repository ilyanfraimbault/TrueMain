using AwesomeAssertions;
using Data.ItemContext;

namespace TrueMain.UnitTests;

/// <summary>
/// The pure half of the item-context fold (#1450): turning nine measured champion profiles
/// into the banded situations an item's pick rate is measured against.
/// </summary>
public sealed class DraftAxisEvaluatorTests
{
    private static readonly DraftAxisThresholds Thresholds = new();

    [Fact]
    public void TheTeamDamageShareIsWeightedByWhoActuallyDealsTheDamage()
    {
        // Four supports at 100% magic but almost no damage, one carry at 100% physical
        // carrying the team. An unweighted mean would call this a magic-damage team.
        var enemies = new List<ChampionProfileFacts>
        {
            Facts(1, magic: 0d, physical: 1d, damagePerGame: 30_000),
            Facts(2, magic: 1d, physical: 0d, damagePerGame: 2_000),
            Facts(3, magic: 1d, physical: 0d, damagePerGame: 2_000),
            Facts(4, magic: 1d, physical: 0d, damagePerGame: 2_000),
            Facts(5, magic: 1d, physical: 0d, damagePerGame: 2_000),
        };

        var axes = Evaluate(enemies: new DraftSide(enemies, 0));

        axes[ItemContextAxis.EnemyMagicDamage].Should().Be(ItemContextBucket.Low);
        axes[ItemContextAxis.EnemyPhysicalDamage].Should().Be(ItemContextBucket.High);
    }

    [Fact]
    public void CountAxesBandOnTheNumberOfChampions()
    {
        var enemies = new List<ChampionProfileFacts>
        {
            Facts(1, sustainPerMinute: 300),
            Facts(2, sustainPerMinute: 300),
            Facts(3, sustainPerMinute: 0),
            Facts(4, sustainPerMinute: 0),
            Facts(5, sustainPerMinute: 0),
        };

        Evaluate(enemies: new DraftSide(enemies, 0))[ItemContextAxis.EnemySustain]
            .Should().Be(ItemContextBucket.High, "two healers clears the High count");

        var one = enemies.Select((facts, index) => index == 0 ? facts : facts with { SustainPerMinute = 0 }).ToList();
        Evaluate(enemies: new DraftSide(one, 0))[ItemContextAxis.EnemySustain]
            .Should().Be(ItemContextBucket.Mid, "one healer is neither end");

        var none = enemies.Select(facts => facts with { SustainPerMinute = 0 }).ToList();
        Evaluate(enemies: new DraftSide(none, 0))[ItemContextAxis.EnemySustain]
            .Should().Be(ItemContextBucket.Low);
    }

    [Fact]
    public void OneUnprofiledEnemyIsToleratedButTwoDropTheWholeSide()
    {
        var four = Enumerable.Range(1, 4).Select(id => Facts(id)).ToList();
        Evaluate(enemies: new DraftSide(four, 1)).Should().ContainKey(ItemContextAxis.EnemyMagicDamage);

        var three = Enumerable.Range(1, 3).Select(id => Facts(id)).ToList();
        var axes = Evaluate(enemies: new DraftSide(three, 2));
        axes.Should().NotContainKey(ItemContextAxis.EnemyMagicDamage);
        axes.Should().NotContainKey(ItemContextAxis.EnemySustain);
    }

    [Fact]
    public void AnAxisThatCannotBeComputedIsAbsent_NeverDefaulted()
    {
        // No lane opponent, no gold reading, and enemies whose range Data Dragon never
        // answered: three axes that must simply not be there.
        var enemies = Enumerable.Range(1, 5).Select(id => Facts(id) with { IsRanged = null }).ToList();

        var axes = Evaluate(enemies: new DraftSide(enemies, 0));

        axes.Should().NotContainKey(ItemContextAxis.EnemyMelee);
        axes.Should().NotContainKey(ItemContextAxis.OpponentRanged);
        axes.Should().NotContainKey(ItemContextAxis.OpponentLanePressure);
        axes.Should().NotContainKey(ItemContextAxis.OwnGoldLeadAt15);
    }

    [Fact]
    public void TheLaneOpponentCarriesItsOwnAxes()
    {
        var opponent = Facts(77, magic: 0.9d, physical: 0.1d) with
        {
            IsRanged = true,
            GoldLeadAt10 = 400d,
            SustainPerMinute = 500d,
        };

        var axes = Evaluate(opponent: opponent);

        axes[ItemContextAxis.OpponentRanged].Should().Be(ItemContextBucket.High);
        axes[ItemContextAxis.OpponentMagicDamage].Should().Be(ItemContextBucket.High);
        axes[ItemContextAxis.OpponentLanePressure].Should().Be(ItemContextBucket.High, "a champion 400 gold up at 10 is a bully");
        axes[ItemContextAxis.OpponentSustain].Should().Be(ItemContextBucket.High);
    }

    [Fact]
    public void BinaryAxesHaveNoMiddle()
    {
        var melee = Facts(88) with { IsRanged = false, SustainPerMinute = 0d };

        var axes = Evaluate(opponent: melee);

        axes[ItemContextAxis.OpponentRanged].Should().Be(ItemContextBucket.Low);
        axes[ItemContextAxis.OpponentSustain].Should().Be(ItemContextBucket.Low);
    }

    [Fact]
    public void TheOwnGoldLeadUsesTheSiteWideLaneEdges()
    {
        Evaluate(goldLead: 900d)[ItemContextAxis.OwnGoldLeadAt15].Should().Be(ItemContextBucket.High);
        Evaluate(goldLead: 0d)[ItemContextAxis.OwnGoldLeadAt15].Should().Be(ItemContextBucket.Mid);
        Evaluate(goldLead: -900d)[ItemContextAxis.OwnGoldLeadAt15].Should().Be(ItemContextBucket.Low);
    }

    private static IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> Evaluate(
        DraftSide? enemies = null,
        DraftSide? allies = null,
        ChampionProfileFacts? opponent = null,
        double? goldLead = null)
        => DraftAxisEvaluator.Evaluate(
            new DraftContext(
                enemies ?? new DraftSide([], 5),
                allies ?? new DraftSide([], 4),
                opponent,
                goldLead),
            Thresholds);

    private static ChampionProfileFacts Facts(
        int championId,
        double magic = 0.4d,
        double physical = 0.5d,
        double damagePerGame = 20_000d,
        double sustainPerMinute = 0d)
        => new()
        {
            ChampionId = championId,
            Position = "MIDDLE",
            Games = 1_000,
            DamagePerGame = damagePerGame,
            MagicShare = magic,
            PhysicalShare = physical,
            SustainPerMinute = sustainPerMinute,
            CrowdControlPerMinute = 0d,
            TankRate = 0d,
            CritRate = 0d,
            ArmorPenetrationRate = 0d,
            IsRanged = false,
            GoldLeadAt10 = 0d,
        };
}
