using TrueMain.Contracts.Champions;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Mapping.Champions;

public static class ChampionBuildTreeMapper
{
    public static ChampionBuildTreeResponse ToContract(this ChampionBuildTreeReadModel readModel)
        => new()
        {
            ChampionId = readModel.ChampionId,
            Patch = readModel.Patch,
            Position = readModel.Position,
            RiotAccountId = readModel.RiotAccountId,
            PlatformId = readModel.PlatformId,
            TotalGames = readModel.TotalGames,
            Boots = readModel.Boots is null
                ? null
                : new ItemSetOptionResponse
                {
                    ItemIds = readModel.Boots.ItemIds.ToList(),
                    Games = readModel.Boots.Games,
                    PlayRate = readModel.Boots.PlayRate,
                    WinRate = readModel.Boots.WinRate
                },
            Build = readModel.Build.Select(MapNode).ToList()
        };

    private static ChampionBuildTreeNodeResponse MapNode(ChampionBuildTreeNodeReadModel node)
        => new()
        {
            ItemId = node.ItemId,
            Games = node.Games,
            Wins = node.Wins,
            PickRate = node.PickRate,
            Children = node.Children.Select(MapNode).ToList()
        };
}
