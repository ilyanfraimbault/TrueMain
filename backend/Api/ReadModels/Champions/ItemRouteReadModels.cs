namespace TrueMain.ReadModels.Champions;

/// <summary>
/// One component on the way to an item (#2183), at its place in the order the champion's
/// mains buy them — or, when too few games measured it, at its place in the recipe.
/// </summary>
public sealed record ItemRouteStepReadModel
{
    public int ItemId { get; init; }

    /// <summary>The component's full shop price, before anything held is taken off it.</summary>
    public int Cost { get; init; }

    /// <summary>1 for the component bought first.</summary>
    public int Rank { get; init; }

    /// <summary>Share of the measured games that bought it at this rank; null when the order is the recipe's.</summary>
    public double? Share { get; init; }

    /// <summary>True when the order comes from the mains' purchases, false when it is the recipe's.</summary>
    public bool Measured { get; init; }

    /// <summary>The component's own components in the same terms, for an intermediate (Lost Chapter); empty for a basic item.</summary>
    public IReadOnlyList<ItemRouteStepReadModel> Components { get; init; } = [];
}

/// <summary>An item and the order its components are bought in — the champion page's form of the build path.</summary>
public sealed record ChampionItemRouteReadModel
{
    public int ItemId { get; init; }

    /// <summary>Games behind the order: the mains' games that bought at least one component before completing it.</summary>
    public int Games { get; init; }

    public bool Measured { get; init; }

    public IReadOnlyList<ItemRouteStepReadModel> Steps { get; init; } = [];
}
