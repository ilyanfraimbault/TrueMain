namespace Data.Entities;

public class ChampionAggregateStarterItems
{
    public Guid Id { get; set; }
    public Guid ScopeId { get; set; }
    public ChampionAggregateScope Scope { get; set; } = null!;

    public string StarterItemsKey { get; set; } = string.Empty;
    public List<int> StarterItems { get; set; } = [];

    public int Games { get; set; }
    public int Wins { get; set; }
}
