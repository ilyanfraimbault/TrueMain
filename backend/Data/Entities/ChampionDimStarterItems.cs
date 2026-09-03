namespace Data.Entities;

/// <summary>
/// Globally-deduplicated reference row for one unique starter-item basket.
/// Phase 6 dimension table — referenced by <see cref="ChampionAggregatePattern"/>.
///
/// <para>
/// The row's identity is <see cref="CanonicalKey"/>, and Postgres computes it: a stored
/// generated column over <see cref="StarterItems"/> holding the item ids ascending, with
/// multiplicity, under a UNIQUE index. Nothing the writer does can store the same basket
/// twice (#1418).
/// </para>
///
/// <para>
/// The column it replaced was written by the application, joining the items in the
/// analyser's display order — <em>most expensive first</em>. That order is derived from
/// patch-dependent item prices and degrades to 0 when an item's metadata is missing, so
/// the same basket keyed differently across generations and the UNIQUE index, faithfully
/// enforced, let both rows in: 17 baskets were split that way in production. Identity may
/// not depend on data that changes.
/// </para>
/// </summary>
public sealed class ChampionDimStarterItems
{
    public Guid Id { get; set; }

    /// <summary>
    /// The basket in display order — most expensive first — which is what the UI renders.
    /// Free to change with prices, because it no longer carries the row's identity.
    /// </summary>
    public List<int> StarterItems { get; set; } = [];

    /// <summary>
    /// Database-generated: the item ids ascending, joined by <c>-</c>. Read-only here —
    /// assigning it is a compile error rather than a silent divergence from the schema.
    /// </summary>
    public string CanonicalKey { get; private set; } = string.Empty;
}
