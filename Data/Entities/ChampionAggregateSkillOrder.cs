namespace Data.Entities;

public class ChampionAggregateSkillOrder
{
    public Guid Id { get; set; }
    public Guid ScopeId { get; set; }
    public ChampionAggregateScope Scope { get; set; } = null!;

    public string SkillOrderKey { get; set; } = string.Empty;

    public int Games { get; set; }
    public int Wins { get; set; }
}
