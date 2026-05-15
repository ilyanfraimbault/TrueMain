namespace Data.Entities;

/// <summary>
/// Globally-deduplicated reference row for one unique skill order
/// (e.g. "Q-W-E"). Phase 6 dimension table — referenced by
/// <see cref="ChampionAggregatePattern"/>.
/// </summary>
public sealed class ChampionDimSkillOrder
{
    public Guid Id { get; set; }

    public string SkillOrderKey { get; set; } = string.Empty;
}
