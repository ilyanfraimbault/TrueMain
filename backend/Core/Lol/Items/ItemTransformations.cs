using System.Collections.Frozen;

namespace Core.Lol.Items;

/// <summary>
/// Static map of in-inventory item transformations: items that mutate in place
/// (Manamune → Muramana, Tear → Seraph's, etc.) without going through the shop.
/// </summary>
public static class ItemTransformations
{
    /// <summary>
    /// Source item id → final item id, for transformations that happen in the
    /// player inventory rather than via a shop purchase.
    /// </summary>
    public static readonly FrozenDictionary<int, int> Map = new Dictionary<int, int>
    {
        [LolItemIds.Manamune] = LolItemIds.Muramana,
        [LolItemIds.TearOfTheGoddess] = LolItemIds.Seraphs
    }.ToFrozenDictionary();
}
