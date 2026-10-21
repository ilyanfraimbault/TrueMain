namespace TrueMain.ReadModels.Items;

/// <summary>
/// The in-game shop's class of every item that has one — what the desktop kit editor's item
/// picker filters by, as the shop does: Fighter, Marksman, Assassin, Mage, Tank, Support.
/// </summary>
public sealed record ItemClassesResponse
{
    /// <summary>The game data's <c>major.minor</c> patch, or <c>latest</c>.</summary>
    public required string Patch { get; init; }

    /// <summary>Every classed item, by id; an item can have several classes, its main one first.</summary>
    public IReadOnlyList<ItemClassReadModel> Items { get; init; } = [];
}

/// <summary>One item's shop classes.</summary>
public sealed record ItemClassReadModel
{
    public int ItemId { get; init; }

    /// <summary>The class names (<c>Fighter</c>, <c>Marksman</c>, …), the item's main class first.</summary>
    public IReadOnlyList<string> Classes { get; init; } = [];
}
