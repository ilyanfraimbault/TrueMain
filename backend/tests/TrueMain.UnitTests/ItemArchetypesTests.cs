using System.Collections.Frozen;
using AwesomeAssertions;
using Data.BuildFacts;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the category rules behind <see cref="ItemArchetypes"/> (#1449): which
/// CommunityDragon categories make an item crit / armour-pen / on-hit / AP / tank, and
/// which items are not classified at all because they are not a build choice.
/// </summary>
public sealed class ItemArchetypesTests
{
    [Fact]
    public void Classify_ReadsEachArchetypeFromItsCategories()
    {
        Final(3031, "Damage", "CriticalStrike").Should().Be(ItemArchetype.Crit);
        Final(3142, "Damage", "ArmorPenetration", "AbilityHaste").Should().Be(ItemArchetype.ArmorPenetration);
        Final(3153, "Damage", "AttackSpeed", "OnHit", "LifeSteal").Should().Be(ItemArchetype.OnHit);
        Final(3089, "SpellDamage").Should().Be(ItemArchetype.AbilityPower);
        Final(3068, "Health", "Armor", "AbilityHaste").Should().Be(ItemArchetype.Tank);
    }

    [Fact]
    public void Classify_CombinesArchetypes_WhenAnItemBelongsToSeveral()
    {
        // Kraken-style: crit and on-hit on one item.
        Final(6672, "Damage", "AttackSpeed", "CriticalStrike", "OnHit")
            .Should().Be(ItemArchetype.Crit | ItemArchetype.OnHit);
    }

    [Fact]
    public void Classify_DoesNotCallOffensiveHealthItemsTank()
    {
        // Titanic Hydra (Health + Damage) and Rylai's (Health + SpellDamage) carry health
        // beside an offensive stat: a bruiser or a mage item, not a tank one.
        Final(3748, "Health", "Damage", "OnHit").Should().Be(ItemArchetype.OnHit);
        Final(3116, "Health", "SpellDamage", "Slow").Should().Be(ItemArchetype.AbilityPower);
    }

    [Fact]
    public void Classify_TreatsMagicPenetrationAsAbilityPowerOnly()
    {
        // Void Staff has no ArmorPenetration category; an AP pen item is AP, not armour pen.
        Final(3135, "SpellDamage", "MagicPenetration").Should().Be(ItemArchetype.AbilityPower);
        // Serylda's-like AP-flavoured pen never happens, but an ArmorPenetration item that
        // is SpellDamage would be an AP item, not an armour-pen AD one.
        Final(9999, "SpellDamage", "ArmorPenetration").Should().Be(ItemArchetype.AbilityPower);
    }

    [Fact]
    public void Classify_IgnoresItemsThatAreNotBuildChoices()
    {
        var component = Item(1036, isFinal: false, categories: ["Damage"]);
        var boots = Item(3006, isFinal: true, categories: ["AttackSpeed", "Boots"]) with { IsBootsItem = true, IsFinalBoots = true };
        var potion = Item(2003, isFinal: true, categories: ["Consumable", "Health"]) with { IsConsumable = true };
        var starter = Item(1055, isFinal: true, categories: ["Damage", "Health", "Lane"]) with { IsStarterClassItem = true };
        var quest = Item(3877, isFinal: true, categories: ["Health", "GoldPer"]) with { IsSupportQuestCompletion = true };

        foreach (var item in new[] { component, boots, potion, starter, quest })
        {
            ItemArchetypes.Classify(item).Should().Be(ItemArchetype.None, "{0} is not a build choice", item.Id);
        }
    }

    [Fact]
    public void ClassifyInventory_UnionsTheSlots_AndSkipsUnknownIds()
    {
        var metadata = new Dictionary<int, ItemMetadata>
        {
            [3031] = Item(3031, isFinal: true, categories: ["Damage", "CriticalStrike"]),
            [3068] = Item(3068, isFinal: true, categories: ["Health", "Armor"]),
        };

        var archetypes = ItemArchetypes.ClassifyInventory([3031, 0, 3068, 4242, 0, 0], metadata);

        archetypes.Should().Be(ItemArchetype.Crit | ItemArchetype.Tank);
    }

    private static ItemArchetype Final(int id, params string[] categories)
        => ItemArchetypes.Classify(Item(id, isFinal: true, categories: categories));

    private static ItemMetadata Item(int id, bool isFinal, string[] categories)
        => new(id, 3000, true, false, false, false, isFinal, false)
        {
            Categories = categories.ToFrozenSet(StringComparer.Ordinal),
        };
}
