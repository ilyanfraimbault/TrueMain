namespace TrueMain.Contracts.Champions;

public sealed class ChampionBuildTreeResponse
{
    public int ChampionId { get; init; }

    public string? Patch { get; init; }

    public string? Position { get; init; }

    public Guid? RiotAccountId { get; init; }

    public string? PlatformId { get; init; }

    public int TotalGames { get; init; }

    public IReadOnlyList<ChampionBuildTreeNodeResponse> Build { get; init; } = [];
}

public sealed class ChampionBuildTreeNodeResponse
{
    public int ItemId { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    public double PickRate { get; init; }

    public IReadOnlyList<ChampionBuildTreeNodeResponse> Children { get; init; } = [];
}
