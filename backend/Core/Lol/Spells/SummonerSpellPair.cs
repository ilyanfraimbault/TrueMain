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
        if (Spell1Id == (int)SummonerSpellId.Flash || Spell2Id == (int)SummonerSpellId.Flash)
        {
            return Spell1Id == (int)SummonerSpellId.Flash ? this : new SummonerSpellPair(Spell2Id, Spell1Id);
        }

        if (Spell1Id == (int)SummonerSpellId.Smite || Spell2Id == (int)SummonerSpellId.Smite)
        {
            return Spell1Id == (int)SummonerSpellId.Smite ? this : new SummonerSpellPair(Spell2Id, Spell1Id);
        }

        return Canonical();
    }
}
