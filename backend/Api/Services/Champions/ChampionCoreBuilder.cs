using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionCoreBuilder
{
    private const int MaxPreviewBuildItems = 3;

    public static ChampionCoreReadModel Build(
        int sampleSize,
        ChampionAdvancedDetailsReadModel advanced,
        IReadOnlyCollection<ChampionAggregateBuild> builds,
        bool includeBuildPath = true)
    {
        var primaryStarterItems = advanced.StarterItemOptions.FirstOrDefault();
        var primarySummonerSpells = advanced.SummonerSpellOptions.FirstOrDefault();
        var primarySkillOrder = advanced.SkillOrderOptions.FirstOrDefault();
        var primaryRunePage = advanced.RunePageOptions.FirstOrDefault();

        var bootsOption = SelectBoots(builds, sampleSize);
        var buildPathItemIds = includeBuildPath
            ? BuildPrimaryBuildPath(builds)
            : [];

        return new ChampionCoreReadModel
        {
            SampleSize = sampleSize,
            StarterItems = primaryStarterItems,
            Boots = bootsOption,
            BuildPath = buildPathItemIds.Count == 0
                ? null
                : new BuildPathPreviewReadModel { ItemIds = buildPathItemIds },
            SummonerSpells = primarySummonerSpells,
            SkillOrder = primarySkillOrder,
            RunePage = primaryRunePage
        };
    }

    private static ItemSetOptionReadModel? SelectBoots(
        IReadOnlyCollection<ChampionAggregateBuild> builds,
        int sampleSize)
    {
        if (builds.Count == 0 || sampleSize <= 0)
        {
            return null;
        }

        var best = builds
            .Where(build => build.BootsItemId > 0)
            .GroupBy(build => build.BootsItemId)
            .Select(group =>
            {
                var games = group.Sum(build => build.Games);
                return new ItemSetOptionReadModel
                {
                    ItemIds = [group.Key],
                    Games = games,
                    PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(build => build.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.ItemIds[0])
            .FirstOrDefault();

        return best;
    }

    private static IReadOnlyList<int> BuildPrimaryBuildPath(IReadOnlyCollection<ChampionAggregateBuild> builds)
    {
        if (builds.Count == 0)
        {
            return [];
        }

        var roots = new Dictionary<int, MutableBuildNode>();

        foreach (var build in builds)
        {
            var path = ExtractBuildPath(build);
            if (path.Count == 0)
            {
                continue;
            }

            var level = roots;

            foreach (var itemId in path)
            {
                if (!level.TryGetValue(itemId, out var node))
                {
                    node = new MutableBuildNode(itemId);
                    level[itemId] = node;
                }

                node.Games += build.Games;
                node.Wins += build.Wins;
                level = node.Children;
            }
        }

        if (roots.Count == 0)
        {
            return [];
        }

        var previewPath = new List<int>(MaxPreviewBuildItems);
        var current = SelectBestNode(roots.Values);

        while (current is not null && previewPath.Count < MaxPreviewBuildItems)
        {
            previewPath.Add(current.ItemId);
            current = current.Children.Count == 0
                ? null
                : SelectBestNode(current.Children.Values);
        }

        return previewPath;
    }

    private static List<int> ExtractBuildPath(ChampionAggregateBuild build)
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
        .ToList();

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
