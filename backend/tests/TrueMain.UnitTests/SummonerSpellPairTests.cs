using Core.Lol.Spells;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

/// <summary>
/// Locks the read-side priority of <see cref="SummonerSpellPair.OrderedForDisplay"/>:
/// 1. Flash first if present.
/// 2. Smite first if Flash absent.
/// 3. Otherwise numeric ascending.
/// </summary>
public sealed class SummonerSpellPairTests
{
    private const int Flash = SummonerSpellIds.Flash;
    private const int Smite = SummonerSpellIds.Smite;
    private const int Heal = SummonerSpellIds.Heal;
    private const int Ignite = SummonerSpellIds.Ignite;
    private const int Teleport = SummonerSpellIds.Teleport;
    private const int Ghost = SummonerSpellIds.Ghost;
    private const int Cleanse = SummonerSpellIds.Cleanse;
    private const int Exhaust = SummonerSpellIds.Exhaust;

    public static TheoryData<int, int, int, int> DisplayOrderingCases() => new()
    {
        { Flash, Smite, Flash, Smite },
        { Smite, Flash, Flash, Smite },
        { Flash, Heal, Flash, Heal },
        { Heal, Flash, Flash, Heal },
        { Flash, Teleport, Flash, Teleport },
        { Ignite, Flash, Flash, Ignite },

        { Smite, Heal, Smite, Heal },
        { Heal, Smite, Smite, Heal },
        { Smite, Ignite, Smite, Ignite },
        { Cleanse, Smite, Smite, Cleanse },
        { Ghost, Smite, Smite, Ghost },

        { Heal, Ignite, Heal, Ignite },
        { Ignite, Heal, Heal, Ignite },
        { Ghost, Heal, Ghost, Heal },
        { Exhaust, Ignite, Exhaust, Ignite },
        { Cleanse, Ghost, Cleanse, Ghost },

        { Flash, Flash, Flash, Flash },
        { Smite, Smite, Smite, Smite },
        { Heal, Heal, Heal, Heal }
    };

    [Theory]
    [MemberData(nameof(DisplayOrderingCases))]
    public void OrderedForDisplay_applies_flash_then_smite_then_numeric(
        int input1,
        int input2,
        int expected1,
        int expected2)
    {
        var ordered = new SummonerSpellPair(input1, input2).OrderedForDisplay();

        ordered.Spell1Id.Should().Be(expected1);
        ordered.Spell2Id.Should().Be(expected2);
    }

    [Fact]
    public void OrderedForDisplay_is_idempotent()
    {
        var first = new SummonerSpellPair(Heal, Smite).OrderedForDisplay();
        var second = first.OrderedForDisplay();

        second.Should().Be(first);
    }

    [Fact]
    public void OrderedForDisplay_is_order_invariant()
    {
        int[] spells = [Flash, Smite, Heal, Ignite, Teleport, Ghost, Cleanse, Exhaust];

        foreach (var a in spells)
        {
            foreach (var b in spells)
            {
                var direct = new SummonerSpellPair(a, b).OrderedForDisplay();
                var swapped = new SummonerSpellPair(b, a).OrderedForDisplay();

                swapped.Should().Be(direct, because: $"l'ordre d'entrée ({a},{b}) ne doit pas changer le résultat");
            }
        }
    }

    [Theory]
    [InlineData(Flash, Smite, Flash, Smite)]
    [InlineData(Smite, Flash, Flash, Smite)]
    [InlineData(Heal, Smite, Heal, Smite)]
    [InlineData(Smite, Heal, Heal, Smite)]
    [InlineData(Ignite, Heal, Heal, Ignite)]
    public void Canonical_orders_by_min_then_max(int input1, int input2, int expected1, int expected2)
    {
        var canonical = new SummonerSpellPair(input1, input2).Canonical();

        canonical.Spell1Id.Should().Be(expected1);
        canonical.Spell2Id.Should().Be(expected2);
    }
}
