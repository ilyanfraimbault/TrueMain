namespace TrueMain.ReadModels.Champions;

/// <summary>
/// When a champion buys each item, on average, at a position: the mean game time
/// of the first purchase of each item (above the sample floor), ordered earliest
/// first. Computed live from the participants' item-purchase timeline. The
/// caller classifies items (core / boots / consumable) from static item data.
/// </summary>
public sealed record ChampionItemTimingsResponse
{
    public int ChampionId { get; init; }
    public string Position { get; init; } = string.Empty;
    public string? Patch { get; init; }
    public IReadOnlyList<ChampionItemTiming> Items { get; init; } = [];
}

public sealed record ChampionItemTiming
{
    public int ItemId { get; init; }
    public int Games { get; init; }
    /// <summary>Average game time of the first purchase of this item, in seconds.</summary>
    public double AvgSeconds { get; init; }
}
