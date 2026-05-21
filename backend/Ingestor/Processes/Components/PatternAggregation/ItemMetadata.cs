namespace Ingestor.Processes.Components.PatternAggregation;

public sealed record ItemMetadata(
    int Id,
    int PriceTotal,
    bool InStore,
    bool IsConsumable,
    bool IsBootsItem,
    bool IsBaseBoots,
    bool IsFinalItem,
    bool IsFinalBoots)
{
    public bool IsInventoryTransformItem { get; init; }
    public int? TransformFromItemId { get; init; }

    /// <summary>
    /// True for the single in-store starter at the root of the support-quest
    /// chain for this patch (e.g. World Atlas in 16.10). Detected dynamically
    /// from <c>requiredBuffCurrencyName == "SupportItemPurchaseBuff"</c> — no
    /// hardcoded IDs, so future Riot reworks pick up the new root automatically.
    /// </summary>
    public bool IsSupportQuestStarter { get; init; }

    /// <summary>
    /// True for non-in-store transitional items on the support-quest chain
    /// between the starter and the final completion (e.g. Runic Compass,
    /// Bounty of Worlds in 16.10). Detected by walking the <c>specialRecipe</c>
    /// graph upward from the starter.
    /// </summary>
    public bool IsSupportQuestIntermediate { get; init; }

    /// <summary>
    /// True for the in-store leaves at the bottom of the support-quest chain
    /// (e.g. Bloodsong, Solstice Sleigh, Celestial Opposition, Dream Maker,
    /// Zaz'Zak's Realmspike in 16.10). These are inventory transforms of the
    /// starter — they should appear in the starter slot, never in the
    /// build path.
    /// </summary>
    public bool IsSupportQuestCompletion { get; init; }
}
