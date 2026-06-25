namespace TrueMain.Services.Champions;

/// <summary>
/// Shared logic for the "core build path" — the most representative item
/// progression for a <c>(champion, position, firstItem, primaryKeystone)</c>
/// slice. Used by both the per-champion detail builds query and the
/// directory list, so the path shown on each row of the champions index
/// matches the path on that champion's detail page for the same slice.
///
/// The algorithm builds a pruned item-progression tree from every
/// <c>(BuildItem1..6)</c> sequence observed in the slice (low-support nodes
/// dropped, fan-out capped) and walks it greedily, picking at each step the
/// most-popular child, breaking ties by deepest subtree then wins then
/// itemId. The walk stops once the next step's parent-relative pick rate
/// dips below <see cref="ItemPathProbThreshold"/>.
/// </summary>
internal static class ChampionBuildPathAnalyzer
{
    public const int ItemPathMaxDepth = 6;
    public const double ItemPathProbThreshold = 0.20;
    public const int BuildTreeMaxDepth = 6;
    public const int BuildTreeMaxChildrenPerNode = 6;
    public const int BuildTreeMinGames = 2;
    public const double BuildTreeMinPickRate = 0.10;

    /// <summary>
    /// One observed item progression inside a slice. Maps 1:1 onto
    /// <see cref="Data.Entities.ChampionDimBuild"/> with the
    /// <c>BuildItem1..6</c> tail plus the per-row games / wins. The
    /// <c>BuildItem0</c> + keystone are slice-level and live on the
    /// caller — they're not stored on the sequence itself.
    /// </summary>
    public readonly record struct BuildSequence(
        int BuildItem1,
        int BuildItem2,
        int BuildItem3,
        int BuildItem4,
        int BuildItem5,
        int BuildItem6,
        int Games,
        int Wins);

    public sealed class TreeNode(int itemId)
    {
        public int ItemId { get; } = itemId;

        public int Games { get; set; }

        public int Wins { get; set; }

        public Dictionary<int, TreeNode> Children { get; } = [];
    }

    /// <summary>
    /// Build the pruned tree of item progressions. Each level drops nodes
    /// below <see cref="BuildTreeMinGames"/> absolute support and below
    /// <see cref="BuildTreeMinPickRate"/> parent-relative pick rate, then
    /// caps fan-out to <see cref="BuildTreeMaxChildrenPerNode"/>. The
    /// <paramref name="sliceGames"/> argument is the games denominator at
    /// the synthetic root (top of the tree).
    /// </summary>
    public static IReadOnlyList<TreeNode> BuildItemTree(
        IReadOnlyList<BuildSequence> rows,
        int sliceGames)
    {
        var rootChildren = new Dictionary<int, TreeNode>();
        foreach (var row in rows)
        {
            var chain = new[]
            {
                row.BuildItem1, row.BuildItem2, row.BuildItem3,
                row.BuildItem4, row.BuildItem5, row.BuildItem6,
            };
            Dictionary<int, TreeNode> level = rootChildren;
            var depth = 0;
            foreach (var itemId in chain)
            {
                if (itemId <= 0 || depth >= BuildTreeMaxDepth)
                {
                    break;
                }
                if (!level.TryGetValue(itemId, out var node))
                {
                    node = new TreeNode(itemId);
                    level[itemId] = node;
                }
                node.Games += row.Games;
                node.Wins += row.Wins;
                level = node.Children;
                depth++;
            }
        }

        return PruneTreeLevel(rootChildren, sliceGames);
    }

    /// <summary>
    /// Walk the pruned tree starting from <paramref name="firstItemId"/>,
    /// choosing the most-popular child at each step (ties broken by deepest
    /// subtree, then wins, then itemId). Returns the item-id chain plus the
    /// games / wins counts at the final node of the walked path — which may
    /// be the synthetic root when the first step fails the probability
    /// gate, or any node short of <see cref="ItemPathMaxDepth"/> if the
    /// probability gate triggers before max depth. The walk stops at
    /// <see cref="ItemPathMaxDepth"/> or once the next step's
    /// parent-relative pick rate falls below
    /// <see cref="ItemPathProbThreshold"/>.
    /// </summary>
    public static (List<int> ItemIds, int Games, int Wins) WalkPath(
        IReadOnlyList<TreeNode> rootChildren,
        int firstItemId,
        int sliceGames,
        int sliceWins)
    {
        var root = new TreeNode(firstItemId) { Games = sliceGames, Wins = sliceWins };
        foreach (var child in rootChildren)
        {
            root.Children[child.ItemId] = child;
        }

        var path = new List<int> { firstItemId };
        var current = root;
        var finalGames = sliceGames;
        var finalWins = root.Wins;

        while (path.Count <= ItemPathMaxDepth && current.Children.Count > 0)
        {
            var best = current.Children.Values
                .OrderByDescending(node => node.Games)
                .ThenByDescending(MaxDepth)
                .ThenByDescending(node => node.Wins)
                .ThenBy(node => node.ItemId)
                .First();
            var probability = current.Games == 0 ? 0d : (double)best.Games / current.Games;
            if (probability < ItemPathProbThreshold)
            {
                break;
            }
            path.Add(best.ItemId);
            finalGames = best.Games;
            finalWins = best.Wins;
            current = best;
        }

        return (path, finalGames, finalWins);
    }

    private static IReadOnlyList<TreeNode> PruneTreeLevel(
        IDictionary<int, TreeNode> level,
        int parentGames)
    {
        var kept = level.Values
            .Where(node => node.Games >= BuildTreeMinGames)
            .Where(node => parentGames == 0 || (double)node.Games / parentGames >= BuildTreeMinPickRate)
            .OrderByDescending(node => node.Games)
            .ThenByDescending(node => node.Wins)
            .ThenBy(node => node.ItemId)
            .Take(BuildTreeMaxChildrenPerNode)
            .ToList();

        foreach (var node in kept)
        {
            var prunedChildren = PruneTreeLevel(node.Children, node.Games);
            node.Children.Clear();
            foreach (var child in prunedChildren)
            {
                node.Children[child.ItemId] = child;
            }
        }

        return kept;
    }

    private static int MaxDepth(TreeNode node)
    {
        if (node.Children.Count == 0)
        {
            return 0;
        }
        var best = 0;
        foreach (var child in node.Children.Values)
        {
            var d = MaxDepth(child);
            if (d > best)
            {
                best = d;
            }
        }
        return 1 + best;
    }
}
