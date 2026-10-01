using Data.Entities;

namespace Data.ItemContext;

/// <summary>One candidate of one branch, with every term the model holds for it.</summary>
/// <param name="ItemId">The item a game could complete next on this branch.</param>
/// <param name="BaseLogShare">Natural log of its share of the branch.</param>
/// <param name="Games">Games of the branch that took it.</param>
/// <param name="Wins">Of which won.</param>
/// <param name="Lifts">Log-ratio shift of its share per situation; a missing situation shifts nothing.</param>
public sealed record NextItemCandidate(
    int ItemId,
    double BaseLogShare,
    int Games,
    int Wins,
    IReadOnlyDictionary<(ItemContextAxis Axis, ItemContextBucket Bucket), double> Lifts);

/// <summary>One branch of the build tree: the decision a game faces after <see cref="ParentItemId"/>.</summary>
/// <param name="Slot">Build or boots.</param>
/// <param name="ParentItemId">The item the decision follows, 0 at the root.</param>
/// <param name="BranchGames">Games of the branch behind the base shares.</param>
/// <param name="PatchWindow">Patches behind the base shares.</param>
/// <param name="Candidates">Every item the branch continues into.</param>
public sealed record NextItemBranch(
    ItemContextSlot Slot,
    int ParentItemId,
    int BranchGames,
    int PatchWindow,
    IReadOnlyList<NextItemCandidate> Candidates);

/// <summary>
/// The next-item model of one champion at one position on one patch (#1749), assembled
/// from its <see cref="ChampionNextItemTerm"/> rows. Immutable once built, so a read can
/// cache it and answer every purchase of every game against it without touching the
/// database again.
/// </summary>
public sealed class NextItemModel
{
    private readonly Dictionary<(ItemContextSlot, int), NextItemBranch> _branches;
    private readonly HashSet<int> _buildItems;

    private NextItemModel(Dictionary<(ItemContextSlot, int), NextItemBranch> branches)
    {
        _branches = branches;
        _buildItems = [.. branches.Values
            .Where(branch => branch.Slot == ItemContextSlot.Build)
            .SelectMany(branch => branch.Candidates)
            .Select(candidate => candidate.ItemId)];
    }

    public static NextItemModel Empty { get; } = new([]);

    public bool IsEmpty => _branches.Count == 0;

    public NextItemBranch? Branch(ItemContextSlot slot, int parentItemId)
        => _branches.GetValueOrDefault((slot, parentItemId));

    public static NextItemModel From(IEnumerable<ChampionNextItemTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var branches = new Dictionary<(ItemContextSlot, int), NextItemBranch>();

        foreach (var branch in terms.GroupBy(term => (term.Slot, term.ParentItemId)))
        {
            var candidates = new List<NextItemCandidate>();
            var branchGames = 0;
            var patchWindow = 1;

            foreach (var item in branch.GroupBy(term => term.ItemId))
            {
                // A candidate exists through its base row; lift rows without one are
                // the leftovers of an item the served patch no longer builds.
                var baseTerm = item.FirstOrDefault(term => term.Axis == ItemContextAxis.Overall);
                if (baseTerm is null)
                {
                    continue;
                }

                branchGames = Math.Max(branchGames, baseTerm.BranchGames);
                patchWindow = Math.Max(patchWindow, baseTerm.PatchWindow);

                candidates.Add(new NextItemCandidate(
                    item.Key,
                    baseTerm.Weight,
                    baseTerm.Games,
                    baseTerm.Wins,
                    item
                        .Where(term => term.Axis != ItemContextAxis.Overall)
                        .ToDictionary(term => (term.Axis, term.Bucket), term => term.Weight)));
            }

            if (candidates.Count > 0)
            {
                branches[branch.Key] = new NextItemBranch(
                    branch.Key.Slot, branch.Key.ParentItemId, branchGames, patchWindow, candidates);
            }
        }

        return new NextItemModel(branches);
    }

    /// <summary>
    /// The build branches a game has passed through, from the items it has completed in
    /// order: the root first, then every item the game re-anchored on.
    /// </summary>
    /// <remarks>
    /// The current branch — the last of the path — is the <b>last completed item the model
    /// has a branch for</b>. A build that stays on the tree walks it edge by edge and lands
    /// where the page's tree would; one that leaves it (an item the cohort never builds at
    /// that step) re-anchors on the latest item that is a branch of its own, rather than
    /// falling back to the root and recommending a first item to a player holding four.
    /// <c>OnTree</c> says which of the two happened. The earlier entries are the fallbacks a
    /// caller walks back through when the current branch has nothing left to offer.
    /// </remarks>
    public (IReadOnlyList<int> Path, bool OnTree) ResolveBuildPath(IReadOnlyList<int> completedInOrder)
    {
        ArgumentNullException.ThrowIfNull(completedInOrder);

        var path = new List<int> { 0 };
        var onTree = true;

        foreach (var itemId in completedInOrder)
        {
            if (!IsBuildItem(itemId))
            {
                continue;
            }

            var isChild = Branch(ItemContextSlot.Build, path[^1])?.Candidates.Any(c => c.ItemId == itemId) ?? false;
            if (!isChild)
            {
                onTree = false;
            }

            if (_branches.ContainsKey((ItemContextSlot.Build, itemId)) && path[^1] != itemId)
            {
                path.Add(itemId);
            }
        }

        return (path, onTree);
    }

    /// <summary>Whether the cohort ever completes this item as a build step, on any branch.</summary>
    public bool IsBuildItem(int itemId) => _buildItems.Contains(itemId);

    /// <summary>Whether this item is one of the boots the cohort settles on.</summary>
    public bool IsBoots(int itemId)
        => Branch(ItemContextSlot.Boots, 0)?.Candidates.Any(candidate => candidate.ItemId == itemId) ?? false;
}
