namespace Data.Entities;

/// <summary>
/// Globally-deduplicated reference row for one unique starter-item set.
/// Phase 6 dimension table — referenced by
/// <see cref="ChampionAggregatePattern"/>. The canonical
/// <see cref="StarterItemsKey"/> drives the UNIQUE constraint so EF doesn't
/// have to compare JSONB arrays for equality during get-or-create.
/// </summary>
public sealed class ChampionDimStarterItems
{
    public Guid Id { get; set; }

    public string StarterItemsKey { get; set; } = string.Empty;
    public List<int> StarterItems { get; set; } = [];
}
