using System.Collections.Frozen;

namespace Data.BuildFacts;

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

    /// <summary>
    /// True for starter-class items meant to be bought at game start: Doran's
    /// (Blade/Ring/Shield/Bow/Helm), Cull, jungle pets, ARAM Guardian's, etc.
    /// Detected dynamically per patch via the (Lane|Jungle) semantic category
    /// combined with structural markers (no recipe, no upgrade, cheap,
    /// in-store, non-consumable, non-boots). These should never appear in
    /// BuildItem0..6 — they belong in the starter slot, regardless of when
    /// in the game they were purchased.
    /// </summary>
    public bool IsStarterClassItem { get; init; }

    /// <summary>
    /// CommunityDragon's semantic categories for the item, verbatim ("Armor",
    /// "SpellBlock", "CriticalStrike", "ArmorPenetration", "OnHit", "SpellDamage",
    /// "Tenacity", ...). The raw material of <see cref="ItemArchetypes"/> (#1449) and of
    /// the situational whitelist (#1450), kept as the source publishes them rather than
    /// pre-digested into booleans so a new consumer needs no provider change.
    /// </summary>
    public IReadOnlySet<string> Categories { get; init; } = FrozenSet<string>.Empty;

    /// <summary>
    /// True when the item's description mentions Grievous Wounds. Categories carry no
    /// anti-heal marker, so this is the one attribute read from the tooltip text —
    /// verified against the live catalogue to select exactly the anti-heal line
    /// (Oblivion Orb, Morellonomicon, Executioner's Calling, Mortal Reminder,
    /// Chempunk Chainsword, Thornmail and its component).
    /// </summary>
    public bool GrantsGrievousWounds { get; init; }
}
