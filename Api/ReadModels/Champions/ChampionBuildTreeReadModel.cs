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

    public IReadOnlyList<ChampionBuildTreeNodeReadModel> Children { get; init; } = [];
}
