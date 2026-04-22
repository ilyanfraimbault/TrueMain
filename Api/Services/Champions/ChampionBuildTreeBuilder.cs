using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public static class ChampionBuildTreeBuilder
{
    public static IReadOnlyList<ChampionBuildTreeNodeReadModel> Build(
        IReadOnlyCollection<ChampionAggregateBuild> rows,
        int totalGames,
        int maxDepth,
        int minBranchGames)
    {
        if (rows.Count == 0 || totalGames <= 0 || maxDepth <= 0)
        {
            return [];
        }

        var roots = new Dictionary<int, MutableNode>();

        foreach (var row in rows)
        {
            var buildPath = ExtractBuildPath(row, maxDepth);
            if (buildPath.Count == 0)
            {
                continue;
            }

            Dictionary<int, MutableNode> level = roots;

            foreach (var itemId in buildPath)
            {
                if (!level.TryGetValue(itemId, out var node))
                {
                    node = new MutableNode(itemId);
                    level[itemId] = node;
                }

                node.Games += row.Games;
                node.Wins += row.Wins;
                level = node.Children;
            }
        }

        return roots.Values
            .Where(node => node.Games >= minBranchGames)
            .OrderByDescending(node => node.Games)
            .ThenByDescending(node => ComputeRate(node.Wins, node.Games))
            .ThenBy(node => node.ItemId)
            .Select(node => Project(node, totalGames, minBranchGames))
            .ToList();
    }

    private static ChampionBuildTreeNodeReadModel Project(MutableNode node, int parentGames, int minBranchGames)
    {
        return new ChampionBuildTreeNodeReadModel
        {
            ItemId = node.ItemId,
            Games = node.Games,
            Wins = node.Wins,
            PickRate = ComputeRate(node.Games, parentGames),
            Children = node.Children.Values
                .Where(child => child.Games >= minBranchGames)
                .OrderByDescending(child => child.Games)
                .ThenByDescending(child => ComputeRate(child.Wins, child.Games))
                .ThenBy(child => child.ItemId)
                .Select(child => Project(child, node.Games, minBranchGames))
                .ToList()
        };
    }

    private static List<int> ExtractBuildPath(ChampionAggregateBuild build, int maxDepth)
        => new[]
            {
                build.BuildItem0,
                build.BuildItem1,
                build.BuildItem2,
                build.BuildItem3,
                build.BuildItem4,
                build.BuildItem5,
                build.BuildItem6
            }
            .Where(itemId => itemId > 0)
            .Take(maxDepth)
            .ToList();

    private static double ComputeRate(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;

    private sealed class MutableNode
    {
        public MutableNode(int itemId)
        {
            ItemId = itemId;
        }

        public int ItemId { get; }

        public int Games { get; set; }

        public int Wins { get; set; }

        public Dictionary<int, MutableNode> Children { get; } = [];
    }
}
