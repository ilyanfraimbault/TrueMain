namespace Data.Entities;

/// <summary>
/// Globally-deduplicated reference row for one unique rune page (primary
/// tree + keystone + 3 minor perks, secondary tree + 2 minor perks, 3 stat
/// shards). Phase 6 dimension table — referenced by
/// <see cref="ChampionAggregatePattern"/>. The legacy
/// <see cref="ChampionAggregateRunePage.FirstItemId"/> half-correlation is
/// gone here: the build correlation is preserved through the pattern row's
/// <c>BuildId</c> + <c>RunePageId</c> tuple.
/// </summary>
public sealed class ChampionDimRunePage
{
    public Guid Id { get; set; }

    public int PrimaryStyleId { get; set; }
    public int PrimaryKeystoneId { get; set; }
    public int PrimaryPerk1Id { get; set; }
    public int PrimaryPerk2Id { get; set; }
    public int PrimaryPerk3Id { get; set; }

    public int SecondaryStyleId { get; set; }
    public int SecondaryPerk1Id { get; set; }
    public int SecondaryPerk2Id { get; set; }

    public int StatOffense { get; set; }
    public int StatFlex { get; set; }
    public int StatDefense { get; set; }
}
