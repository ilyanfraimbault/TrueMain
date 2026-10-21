namespace Data.Statics;

/// <summary>
/// The classes the in-game shop filters items by — "Fighter", "Marksman", "Assassin", "Mage",
/// "Tank", "Support" — as the bits of an item's <c>mItemAttributes</c> in the game's item data.
/// </summary>
/// <remarks>
/// The game data never names these bits: the mapping was read off the items whose icon file
/// names carry their class (<c>3031_marksman_t3_infinityedge</c>, <c>enchanter</c> for
/// support), which agree with it but for items whose role changed since their icon was drawn.
/// The shop's own filter labels (<c>shop_filter_*</c> in the game's string table) name the six.
/// </remarks>
[Flags]
public enum ItemShopClass
{
    None = 0,
    Fighter = 1,
    Marksman = 2,
    Assassin = 4,
    Tank = 8,
    Mage = 16,
    Support = 32,
}
