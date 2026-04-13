using TrueMain.Contracts.Champions;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Mapping.Champions;

public static class ChampionMapper
{
    public static ChampionResponse ToContract(
        this ChampionFoundationReadModel foundationReadModel,
        ChampionBuildTreeReadModel buildTreeReadModel)
    {
        var starterItems = foundationReadModel.HowToPlay.StarterItemOptions.FirstOrDefault();
        var allStarterItemIds = foundationReadModel.HowToPlay.StarterItemOptions
            .SelectMany(option => option.ItemIds)
            .Where(itemId => itemId > 0)
            .ToHashSet();
        var summonerSpells = foundationReadModel.HowToPlay.SummonerSpellOptions.FirstOrDefault();
        var skillOrder = foundationReadModel.HowToPlay.SkillOrderOptions.FirstOrDefault();

        return new ChampionResponse
        {
            Summary = new ChampionSummaryResponse
            {
                ChampionId = foundationReadModel.Summary.ChampionId,
                Games = foundationReadModel.Summary.Games,
                WinRate = foundationReadModel.Summary.WinRate,
                TrueMainCount = foundationReadModel.Summary.TrueMainCount,
                Position = foundationReadModel.Summary.Position,
                LatestPatchVersion = foundationReadModel.Summary.LatestPatchVersion,
                LastUpdatedAtUtc = foundationReadModel.Summary.LastUpdatedAtUtc
            },
            Core = new ChampionCoreResponse
            {
                SampleSize = foundationReadModel.HowToPlay.SampleSize,
                StarterItems = starterItems is null
                    ? null
                    : MapItemSetOption(starterItems),
                BuildPath = BuildPrimaryPathPreview(buildTreeReadModel, allStarterItemIds),
                SummonerSpells = summonerSpells is null
                    ? null
                    : MapSummonerOption(summonerSpells),
                SkillOrder = skillOrder is null
                    ? null
                    : MapSkillOrderOption(skillOrder)
            },
            Advanced = new ChampionAdvancedDetailsResponse
            {
                StarterItemOptions = foundationReadModel.HowToPlay.StarterItemOptions
                    .Select(MapItemSetOption)
                    .ToList(),
                SummonerSpellOptions = foundationReadModel.HowToPlay.SummonerSpellOptions
                    .Select(MapSummonerOption)
                    .ToList(),
                SkillOrderOptions = foundationReadModel.HowToPlay.SkillOrderOptions
                    .Select(MapSkillOrderOption)
                    .ToList()
            },
            BuildTree = buildTreeReadModel.ToContract()
        };
    }

    private static SummonerSpellOptionResponse MapSummonerOption(SummonerSpellOptionReadModel readModel)
        => new()
        {
            Spell1Id = readModel.Spell1Id,
            Spell2Id = readModel.Spell2Id,
            Games = readModel.Games,
            PlayRate = readModel.PlayRate,
            WinRate = readModel.WinRate
        };

    private static SkillOrderOptionResponse MapSkillOrderOption(SkillOrderOptionReadModel readModel)
        => new()
        {
            Sequence = readModel.Sequence.ToList(),
            Games = readModel.Games,
            PlayRate = readModel.PlayRate,
            WinRate = readModel.WinRate
        };

    private static ItemSetOptionResponse MapItemSetOption(ItemSetOptionReadModel readModel)
        => new()
        {
            ItemIds = readModel.ItemIds.ToList(),
            Games = readModel.Games,
            PlayRate = readModel.PlayRate,
            WinRate = readModel.WinRate
        };

    private static BuildPathPreviewResponse? BuildPrimaryPathPreview(
        ChampionBuildTreeReadModel readModel,
        IReadOnlySet<int> starterItemIds)
    {
        var bestPath = ResolveBestBuildPath(readModel.Build, starterItemIds);
        var itemIds = bestPath.ItemIds;

        return itemIds.Count == 0
            ? null
            : new BuildPathPreviewResponse
            {
                ItemIds = itemIds
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
