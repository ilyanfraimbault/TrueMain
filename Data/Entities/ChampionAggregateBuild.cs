namespace Data.Entities;

public class ChampionAggregateBuild
{
    public Guid Id { get; set; }
    public Guid ScopeId { get; set; }
    public ChampionAggregateScope Scope { get; set; } = null!;

    public int BootsItemId { get; set; }
    public int BuildItem0 { get; set; }
    public int BuildItem1 { get; set; }
    public int BuildItem2 { get; set; }
    public int BuildItem3 { get; set; }
    public int BuildItem4 { get; set; }
    public int BuildItem5 { get; set; }
    public int BuildItem6 { get; set; }

    public int Games { get; set; }
    public int Wins { get; set; }
}
