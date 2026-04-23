namespace TrueMain.ReadModels.Champions;

public sealed class ChampionBuildTreeReadModel
{
    public int ChampionId { get; init; }

    public string? Patch { get; init; }

    public string? Position { get; init; }

    public Guid? RiotAccountId { get; init; }

    public string? PlatformId { get; init; }

    public int TotalGames { get; init; }

    public ItemSetOptionReadModel? Boots { get; init; }

    public IReadOnlyList<ChampionBuildTreeNodeReadModel> Build { get; init; } = [];
}

public sealed class ChampionBuildTreeNodeReadModel
{
    public int ItemId { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    public double PickRate { get; init; }

    /// <summary>
    /// Top rune page played when this item is the first completed build item.
    /// Populated on root nodes only (depth 1); null on deeper branches and
    /// when no rune-page data is correlated with this root. Lets the UI show
    /// a "when rushing X, play rune page Y" badge per build-tree branch.
    /// </summary>
    public RunePageOptionReadModel? RunePage { get; init; }

    public IReadOnlyList<ChampionBuildTreeNodeReadModel> Children { get; init; } = [];
}
