using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionCoreBuilder
{
    public static ChampionCoreReadModel Build(
        ChampionAdvancedDetailsReadModel advancedReadModel,
        ChampionBuildTreeReadModel buildTreeReadModel)
    {
        var starterItems = advancedReadModel.StarterItemOptions.FirstOrDefault();
        var starterItemIds = advancedReadModel.StarterItemOptions
            .SelectMany(option => option.ItemIds)
            .Where(itemId => itemId > 0)
            .ToHashSet();

        return new ChampionCoreReadModel
        {
            SampleSize = advancedReadModel.SampleSize,
            StarterItems = starterItems,
            BuildPathItemIds = ResolveBestBuildPath(buildTreeReadModel.Build, starterItemIds).ItemIds,
            SummonerSpells = advancedReadModel.SummonerSpellOptions.FirstOrDefault(),
            SkillOrder = advancedReadModel.SkillOrderOptions.FirstOrDefault()
        };
    }

    private static CandidateBuildPath ResolveBestBuildPath(
        IReadOnlyList<ChampionBuildTreeNodeReadModel> roots,
        IReadOnlySet<int> starterIds)
    {
        CandidateBuildPath? bestTriple = null;
        CandidateBuildPath? bestPartial = null;

        foreach (var root in roots)
        {
            VisitNode(
                root,
                starterIds,
                [],
                ref bestTriple,
                ref bestPartial);
        }

        return bestTriple ?? bestPartial ?? CandidateBuildPath.Empty;
    }

    private static void VisitNode(
        ChampionBuildTreeNodeReadModel node,
        IReadOnlySet<int> starterIds,
        List<int> currentPath,
        ref CandidateBuildPath? bestTriple,
        ref CandidateBuildPath? bestPartial)
    {
        var nextPath = currentPath;

        if (node.ItemId > 0 && !starterIds.Contains(node.ItemId))
        {
            nextPath = [.. currentPath, node.ItemId];
        }

        if (nextPath.Count >= 3)
        {
            var candidate = new CandidateBuildPath(nextPath.Take(3).ToList(), node.Games, node.Wins);
            if (bestTriple is null || candidate.IsBetterThan(bestTriple))
            {
                bestTriple = candidate;
            }
        }

        var canExtendPath = node.Children.Any(child => HasSelectableDescendant(child, starterIds));

        if (nextPath.Count > 0 && !canExtendPath && bestTriple is null)
        {
            var candidate = new CandidateBuildPath(nextPath, node.Games, node.Wins);
            if (bestPartial is null || candidate.IsBetterThan(bestPartial))
            {
                bestPartial = candidate;
            }
        }

        if (nextPath.Count >= 3)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            VisitNode(
                child,
                starterIds,
                nextPath,
                ref bestTriple,
                ref bestPartial);
        }
    }

    private static bool HasSelectableDescendant(
        ChampionBuildTreeNodeReadModel node,
        IReadOnlySet<int> starterIds)
    {
        if (node.ItemId > 0 && !starterIds.Contains(node.ItemId))
        {
            return true;
        }

        return node.Children.Any(child => HasSelectableDescendant(child, starterIds));
    }

    private sealed record CandidateBuildPath(IReadOnlyList<int> ItemIds, int Games, int Wins)
    {
        public static CandidateBuildPath Empty { get; } = new([], 0, 0);

        public bool IsBetterThan(CandidateBuildPath other)
        {
            if (Games != other.Games)
            {
                return Games > other.Games;
            }

            if (ItemIds.Count != other.ItemIds.Count)
            {
                return ItemIds.Count > other.ItemIds.Count;
            }

            var winRate = Games > 0 ? (double)Wins / Games : 0d;
            var otherWinRate = other.Games > 0 ? (double)other.Wins / other.Games : 0d;

            if (winRate != otherWinRate)
            {
                return winRate > otherWinRate;
            }

            return string.Join("-", ItemIds).CompareTo(string.Join("-", other.ItemIds), StringComparison.Ordinal) < 0;
        }
    }
}
