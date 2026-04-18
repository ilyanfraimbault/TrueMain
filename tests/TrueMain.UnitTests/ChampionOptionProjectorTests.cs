using FluentAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

/// <summary>
/// Tests verrouillant le comportement actuel de <see cref="ChampionOptionProjector.NormalizeSummonerPair"/>.
///
/// Cette fonction implémente une priorité spécifique côté lecture :
/// 1. Si Flash (4) est présent, il est placé en premier.
/// 2. Sinon si Smite (11) est présent, il est placé en premier.
/// 3. Sinon, ordre numérique croissant.
///
/// Cette sémantique est intentionnellement DIFFÉRENTE de la canonicalisation
/// utilisée à l'écriture (cf. ChampionPatternNormalization.NormalizeSummonerPair
/// dans Ingestor, qui se contente d'ordonner par (min, max)).
/// L'écart est sain dans le pipeline actuel parce que le regroupement à l'écriture
/// canonicalise déterministiquement les paires, et la fonction de lecture est une
/// transformation pure appliquée à ces tuples canoniques. Il faut malgré tout
/// verrouiller son comportement avant le refactor Phase 1.2 qui les fusionnera
/// proprement dans <c>Core.Lol.Spells.SummonerSpellPair</c>.
/// </summary>
public sealed class ChampionOptionProjectorTests
{
    private const int FlashId = 4;
    private const int SmiteId = 11;
    private const int HealId = 7;
    private const int IgniteId = 14;
    private const int TeleportId = 12;
    private const int GhostId = 6;
    private const int CleanseId = 1;
    private const int ExhaustId = 3;

    public static TheoryData<int, int, int, int> NormalizationCases() => new()
    {
        // Flash a priorité absolue, peu importe l'ordre d'entrée
        { FlashId, SmiteId, FlashId, SmiteId },
        { SmiteId, FlashId, FlashId, SmiteId },
        { FlashId, HealId, FlashId, HealId },
        { HealId, FlashId, FlashId, HealId },
        { FlashId, TeleportId, FlashId, TeleportId },
        { IgniteId, FlashId, FlashId, IgniteId },

        // Smite a priorité quand Flash absent — c'est le cas critique (Smite, Heal)
        { SmiteId, HealId, SmiteId, HealId },
        { HealId, SmiteId, SmiteId, HealId },
        { SmiteId, IgniteId, SmiteId, IgniteId },
        { CleanseId, SmiteId, SmiteId, CleanseId },
        { GhostId, SmiteId, SmiteId, GhostId },

        // Aucun Flash ni Smite : ordre numérique croissant
        { HealId, IgniteId, HealId, IgniteId },
        { IgniteId, HealId, HealId, IgniteId },
        { GhostId, HealId, GhostId, HealId },
        { ExhaustId, IgniteId, ExhaustId, IgniteId },
        { CleanseId, GhostId, CleanseId, GhostId },

        // Doublon (théoriquement impossible en jeu mais on défend la pure-function)
        { FlashId, FlashId, FlashId, FlashId },
        { SmiteId, SmiteId, SmiteId, SmiteId },
        { HealId, HealId, HealId, HealId }
    };

    [Theory]
    [MemberData(nameof(NormalizationCases))]
    public void NormalizeSummonerPair_ShouldApplyFlashThenSmiteThenNumericOrdering(
        int input1,
        int input2,
        int expectedSpell1,
        int expectedSpell2)
    {
        var (actualSpell1, actualSpell2) = ChampionOptionProjector.NormalizeSummonerPair(input1, input2);

        actualSpell1.Should().Be(expectedSpell1);
        actualSpell2.Should().Be(expectedSpell2);
    }

    [Fact]
    public void NormalizeSummonerPair_IsIdempotent()
    {
        var first = ChampionOptionProjector.NormalizeSummonerPair(HealId, SmiteId);
        var second = ChampionOptionProjector.NormalizeSummonerPair(first.spell1Id, first.spell2Id);

        second.Should().Be(first);
    }

    [Fact]
    public void NormalizeSummonerPair_ShouldBeOrderInvariantForAnyPair()
    {
        // Pour toute paire, normaliser (a, b) doit donner le même résultat que normaliser (b, a).
        int[] spells = [FlashId, SmiteId, HealId, IgniteId, TeleportId, GhostId, CleanseId, ExhaustId];

        foreach (var a in spells)
        {
            foreach (var b in spells)
            {
                var direct = ChampionOptionProjector.NormalizeSummonerPair(a, b);
                var swapped = ChampionOptionProjector.NormalizeSummonerPair(b, a);

                swapped.Should().Be(direct, because: $"l'ordre d'entrée ({a},{b}) ne doit pas changer le résultat");
            }
        }
    }
}
