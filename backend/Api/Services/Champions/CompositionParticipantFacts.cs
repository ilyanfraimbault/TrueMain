namespace TrueMain.Services.Champions;

/// <summary>
/// Build facts extracted from one top-K participant — the aggregation input of
/// <see cref="CompositionBuildAggregator"/>. Collections may be empty and the
/// rune page null when the game lacks the underlying data (no timeline events,
/// no perk selections, unavailable item metadata); the aggregator treats those
/// as abstentions for the affected dimension, never as errors.
/// </summary>
public sealed record CompositionParticipantFacts
{
    public required bool Win { get; init; }

    /// <summary>Completed build items in completion order (boots excluded).</summary>
    public IReadOnlyList<int> BuildItems { get; init; } = [];

    /// <summary>Boots item id, 0 when none was resolved.</summary>
    public int BootsItemId { get; init; }

    public IReadOnlyList<int> StarterItems { get; init; } = [];

    /// <summary>Canonical (min, max) summoner spell pair.</summary>
    public int Spell1Id { get; init; }

    public int Spell2Id { get; init; }

    /// <summary>Max-order key (<c>"Q-W-E"</c>), empty when unknown.</summary>
    public string SkillOrderKey { get; init; } = string.Empty;

    public CompositionRunePageFacts? RunePage { get; init; }
}

/// <summary>
/// Full rune page of one participant. A record so value equality groups
/// identical pages during aggregation.
/// </summary>
public sealed record CompositionRunePageFacts(
    int PrimaryStyleId,
    int PrimaryKeystoneId,
    int PrimaryPerk1Id,
    int PrimaryPerk2Id,
    int PrimaryPerk3Id,
    int SecondaryStyleId,
    int SecondaryPerk1Id,
    int SecondaryPerk2Id,
    int StatOffense,
    int StatFlex,
    int StatDefense);
