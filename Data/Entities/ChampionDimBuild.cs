namespace Data.Entities;

/// <summary>
/// Globally-deduplicated reference row for one unique build (boots + 6 item
/// slots). Phase 6 dimension table — referenced by
/// <see cref="ChampionAggregatePattern"/>. Carries no scope, no Games / Wins:
/// counts live on the pattern row, this table just defines the "what".
/// </summary>
public sealed class ChampionDimBuild
{
    public Guid Id { get; set; }

    public int BootsItemId { get; set; }
    public int BuildItem0 { get; set; }
    public int BuildItem1 { get; set; }
    public int BuildItem2 { get; set; }
    public int BuildItem3 { get; set; }
    public int BuildItem4 { get; set; }
    public int BuildItem5 { get; set; }
    public int BuildItem6 { get; set; }
}
