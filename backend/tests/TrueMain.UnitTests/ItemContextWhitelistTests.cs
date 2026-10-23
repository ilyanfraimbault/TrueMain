using System.Collections.Frozen;
using AwesomeAssertions;
using Data.BuildFacts;
using Data.ItemContext;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the guard the whole feature's credibility rests on (#1450): an item is only ever
/// offered the situations it could mechanically answer, so a significant correlation on
/// anything else can never reach a sentence.
/// </summary>
public sealed class ItemContextWhitelistTests
{
    [Fact]
    public void MagicResistAnswersMagicDamage_AndNothingPhysical()
    {
        var axes = Build("SpellBlock", "Health");

        axes.Should().Contain(ItemContextAxis.EnemyMagicDamage);
        axes.Should().Contain(ItemContextAxis.OpponentMagicDamage);
        axes.Should().NotContain(ItemContextAxis.EnemyPhysicalDamage,
            "magic resistance does not answer physical damage — it is the whole point of the guard");
    }

    [Fact]
    public void ArmourAnswersThePhysicalFamily()
    {
        var axes = Build("Armor", "Health");

        axes.Should().Contain([
            ItemContextAxis.EnemyPhysicalDamage,
            ItemContextAxis.EnemyCrit,
            ItemContextAxis.EnemyArmorPenetration,
            ItemContextAxis.EnemyMelee,
        ]);
    }

    [Fact]
    public void TenacityAnswersCrowdControl_AndGrievousWoundsAnswerSustain()
    {
        Build("SpellBlock", "Tenacity").Should().Contain(ItemContextAxis.EnemyCrowdControl);

        var antiHeal = ItemContextWhitelist.For(
            Item(3123, "Damage") with { GrantsGrievousWounds = true }, ItemContextSlot.Build);
        antiHeal.Should().Contain([ItemContextAxis.EnemySustain, ItemContextAxis.OpponentSustain]);
    }

    [Fact]
    public void PenetrationAnswersTheFrontline()
    {
        Build("SpellDamage", "MagicPenetration").Should().Contain(ItemContextAxis.EnemyFrontline);
        Build("Damage", "ArmorPenetration").Should().Contain(ItemContextAxis.EnemyFrontline);
    }

    [Fact]
    public void HealthWithNoResistanceAnswersBothDamageTypes()
    {
        // Warmog's-shaped: raw health and nothing else, which is what you buy when the
        // threat is everything at once.
        Build("Health", "HealthRegen").Should().Contain([
            ItemContextAxis.EnemyPhysicalDamage,
            ItemContextAxis.EnemyMagicDamage,
        ]);
    }

    [Fact]
    public void AResistedItemDoesNotBorrowTheOtherDamageAxisThroughItsHealth()
    {
        // Sunfire-shaped: armour and health. The armour is why it is bought; the health
        // must not also make it an answer to magic damage.
        Build("Health", "Armor").Should().NotContain(ItemContextAxis.EnemyMagicDamage);

        // Spirit Visage-shaped, the mirror case.
        Build("Health", "SpellBlock").Should().NotContain(ItemContextAxis.EnemyPhysicalDamage);
    }

    [Fact]
    public void AnOffensiveHealthItemAnswersNeither()
    {
        // Titanic-shaped: the health rides with attack damage, so nobody buys it because
        // the enemy team is AP.
        Build("Health", "Damage", "OnHit").Should().NotContain([
            ItemContextAxis.EnemyMagicDamage,
            ItemContextAxis.EnemyPhysicalDamage,
        ]);
    }

    [Fact]
    public void AStarterIsEligibleOnEveryLaneOpponentAxis_WhateverItIsMadeOf()
    {
        var axes = ItemContextWhitelist.For(Item(1055, "Damage", "Health", "Lane"), ItemContextSlot.Starter);

        axes.Should().Contain([
            ItemContextAxis.OpponentRanged,
            ItemContextAxis.OpponentLanePressure,
            ItemContextAxis.OpponentMagicDamage,
            ItemContextAxis.OpponentSustain,
        ]);
    }

    [Fact]
    public void BootsGetNoBlanket_SoAttackSpeedBootsCannotBeExplainedByTheEnemysDamageType()
    {
        var berserkers = ItemContextWhitelist.For(
            Item(3006, "AttackSpeed", "Boots"), ItemContextSlot.Boots);
        berserkers.Should().NotContain([
            ItemContextAxis.EnemyMagicDamage,
            ItemContextAxis.EnemyPhysicalDamage,
            ItemContextAxis.EnemyCrowdControl,
        ]);

        var mercuries = ItemContextWhitelist.For(
            Item(3111, "SpellBlock", "Tenacity", "Boots"), ItemContextSlot.Boots);
        mercuries.Should().Contain([
            ItemContextAxis.EnemyMagicDamage,
            ItemContextAxis.EnemyCrowdControl,
        ]);

        var steelcaps = ItemContextWhitelist.For(
            Item(3047, "Armor", "Boots"), ItemContextSlot.Boots);
        steelcaps.Should().Contain(ItemContextAxis.EnemyPhysicalDamage);
    }

    [Fact]
    public void OnlyBuildItemsCarryTheInGameGoldAxis()
    {
        Build("Damage").Should().Contain(ItemContextAxis.OwnGoldLeadAt15);
        ItemContextWhitelist.For(Item(3006, "AttackSpeed", "Boots"), ItemContextSlot.Boots)
            .Should().NotContain(ItemContextAxis.OwnGoldLeadAt15);
        ItemContextWhitelist.For(Item(1055, "Damage", "Lane"), ItemContextSlot.Starter)
            .Should().NotContain(ItemContextAxis.OwnGoldLeadAt15);
    }

    [Fact]
    public void AnItemThatAnswersNothingGetsNoAxes()
    {
        // Ability haste and mana: real stats, no situation this vocabulary can express.
        ItemContextWhitelist.For(Item(3158, "AbilityHaste", "Boots"), ItemContextSlot.Boots)
            .Should().BeEmpty();
    }

    [Fact]
    public void ADefensiveItemAnswersOnlyTheThreatEnd()
    {
        var magicResist = Item(9001, "SpellBlock", "Health");

        Answers(magicResist, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High).Should().BeTrue();
        Answers(magicResist, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low).Should().BeFalse(
            "magic resistance is never the answer to a team with little magic damage");
        Answers(Item(9002, "Armor"), ItemContextAxis.EnemyMelee, ItemContextBucket.Low).Should().BeFalse();
    }

    [Fact]
    public void LordDominiksNeverAnswersATeamShortOfMagicDamage()
    {
        // The card that read "Lord Dominik's, when the team has little magic damage".
        var dominiks = Item(3036, "Damage", "ArmorPenetration", "CriticalStrike");

        Answers(dominiks, ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low).Should().BeFalse();
        Answers(dominiks, ItemContextAxis.AllyMagicDamage, ItemContextBucket.High).Should().BeTrue();
        Answers(dominiks, ItemContextAxis.EnemyFrontline, ItemContextBucket.High).Should().BeTrue();
        Answers(dominiks, ItemContextAxis.EnemyFrontline, ItemContextBucket.Low).Should().BeFalse();
    }

    [Fact]
    public void AnAbilityPowerItemAnswersATeamShortOfMagicDamage()
    {
        var nashors = Item(3115, "SpellDamage", "AttackSpeed", "OnHit");

        Answers(nashors, ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low).Should().BeTrue();
        Answers(nashors, ItemContextAxis.AllyMagicDamage, ItemContextBucket.High).Should().BeFalse();
    }

    [Fact]
    public void TwoSidedAxesAnswerBothEnds_AndAnIneligibleAxisNeither()
    {
        var item = Item(9001, "Damage");

        Answers(item, ItemContextAxis.OwnGoldLeadAt15, ItemContextBucket.Low).Should().BeTrue();
        Answers(item, ItemContextAxis.OwnGoldLeadAt15, ItemContextBucket.High).Should().BeTrue();
        Answers(item, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High).Should().BeFalse(
            "the end rule never widens what the item is eligible for");

        var doransShield = Item(1054, "Health", "Lane");
        ItemContextWhitelist.Answers(doransShield, ItemContextSlot.Starter, ItemContextAxis.OpponentRanged, ItemContextBucket.Low)
            .Should().BeTrue();
        ItemContextWhitelist.Answers(doransShield, ItemContextSlot.Starter, ItemContextAxis.OpponentRanged, ItemContextBucket.High)
            .Should().BeTrue();
    }

    private static bool Answers(ItemMetadata item, ItemContextAxis axis, ItemContextBucket bucket)
        => ItemContextWhitelist.Answers(item, ItemContextSlot.Build, axis, bucket);

    private static IReadOnlySet<ItemContextAxis> Build(params string[] categories)
        => ItemContextWhitelist.For(Item(9001, categories), ItemContextSlot.Build);

    private static ItemMetadata Item(int id, params string[] categories)
        => new(id, 3000, true, false, categories.Contains("Boots"), false, true, categories.Contains("Boots"))
        {
            Categories = categories.ToFrozenSet(StringComparer.Ordinal),
        };
}
