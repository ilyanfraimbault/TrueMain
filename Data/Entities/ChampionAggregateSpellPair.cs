namespace Data.Entities;

public class ChampionAggregateSpellPair
{
    public Guid Id { get; set; }
    public Guid ScopeId { get; set; }
    public ChampionAggregateScope Scope { get; set; } = null!;

    public int Spell1Id { get; set; }
    public int Spell2Id { get; set; }

    public int Games { get; set; }
    public int Wins { get; set; }
}
