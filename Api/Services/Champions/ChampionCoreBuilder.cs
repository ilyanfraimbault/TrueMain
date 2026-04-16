using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionCoreBuilder
{
    private const int MaxPreviewBuildItems = 3;

    public static ChampionCoreReadModel Build(
        ChampionFoundationReadModel foundationReadModel,
        bool includeBuildPath = true)
    {
        var primaryStarterItems = foundationReadModel.Advanced.StarterItemOptions.FirstOrDefault();
        var correlatedPattern = foundationReadModel.CorrelatedPatterns
            .FirstOrDefault(pattern => pattern.BuildItemIds.Count > 0)
            ?? foundationReadModel.CorrelatedPatterns.FirstOrDefault();

        return new ChampionCoreReadModel
        {
            SampleSize = foundationReadModel.Advanced.SampleSize,
            StarterItems = primaryStarterItems,
            Boots = correlatedPattern?.Boots,
            BuildPathItemIds = includeBuildPath
                ? BuildPrimaryBuildPath(foundationReadModel.CorrelatedPatterns)
                : [],
            SummonerSpells = correlatedPattern?.SummonerSpells,
            SkillOrder = correlatedPattern?.SkillOrder
        };
    }

    private static IReadOnlyList<int> BuildPrimaryBuildPath(
        IReadOnlyList<ChampionCorrelatedPatternReadModel> patterns)
    {
        if (patterns.Count == 0)
        {
            return [];
        }

        var roots = new Dictionary<int, MutableBuildNode>();

        foreach (var pattern in patterns.Where(pattern => pattern.BuildItemIds.Count > 0))
        {
            var level = roots;

            foreach (var itemId in pattern.BuildItemIds)
            {
                if (!level.TryGetValue(itemId, out var node))
                {
                    node = new MutableBuildNode(itemId);
                    level[itemId] = node;
                }

                node.Games += pattern.Games;
                node.Wins += pattern.Wins;
                level = node.Children;
            }
        }

        if (roots.Count == 0)
        {
            return [];
        }

        var path = new List<int>(MaxPreviewBuildItems);
        var current = SelectBestNode(roots.Values);

        while (current is not null && path.Count < MaxPreviewBuildItems)
        {
            path.Add(current.ItemId);
            current = current.Children.Count == 0
                ? null
                : SelectBestNode(current.Children.Values);
        }

        return path;
    }

    private static MutableBuildNode? SelectBestNode(IEnumerable<MutableBuildNode> nodes)
        => nodes
            .OrderByDescending(node => node.Games)
            .ThenByDescending(node => node.Wins)
            .ThenBy(node => node.ItemId)
            .FirstOrDefault();

    private sealed class MutableBuildNode
    {
        public MutableBuildNode(int itemId)
        {
            ItemId = itemId;
        }

        public int ItemId { get; }

        public int Games { get; set; }

        public int Wins { get; set; }

        public Dictionary<int, MutableBuildNode> Children { get; } = [];
    }
}
