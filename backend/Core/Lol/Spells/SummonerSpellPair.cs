namespace Core.Lol.Spells;

/// <summary>
/// A pair of summoner spells. Provides two orderings:
/// <see cref="Canonical"/> for storage / grouping (min, max),
/// <see cref="OrderedForDisplay"/> for read-side projection
/// (Flash > Smite > numeric ascending).
/// </summary>
public readonly record struct SummonerSpellPair(int Spell1Id, int Spell2Id)
{
    public SummonerSpellPair Canonical()
        => Spell1Id <= Spell2Id ? this : new SummonerSpellPair(Spell2Id, Spell1Id);

    public SummonerSpellPair OrderedForDisplay()
    {
        if (Spell1Id == SummonerSpellIds.Flash || Spell2Id == SummonerSpellIds.Flash)
        {
            return Spell1Id == SummonerSpellIds.Flash ? this : new SummonerSpellPair(Spell2Id, Spell1Id);
        }

        if (Spell1Id == SummonerSpellIds.Smite || Spell2Id == SummonerSpellIds.Smite)
        {
            return Spell1Id == SummonerSpellIds.Smite ? this : new SummonerSpellPair(Spell2Id, Spell1Id);
        }

        return Canonical();
    }
}
