using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// Turns the item-context counters into the next-item model's terms (#1749): for every
/// candidate of every branch, its base log-share and the log-ratio by which each draft
/// situation shifts it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Same counters as the verdicts, a different question.</b> A verdict asks whether one
/// situation, alone, explains an item well enough to print a sentence — hence its three
/// floors. The model asks which of the siblings a game in <em>all</em> its situations is
/// most likely to take, so it keeps every bucket and lets the sample decide how far each
/// one moves the share: a bucket's share is shrunk towards the item's overall share by
/// <see cref="NextItemModelOptions.PriorGames"/> pseudo-games before its ratio is taken.
/// A thin bucket therefore costs nothing, and no threshold has to be right.
/// </para>
/// <para>
/// <b>Two windows.</b> The base share is the served patch's alone whenever the branch is
/// deep enough there (<see cref="NextItemModelOptions.MinBaseBranchGames"/>), because the
/// meta moves between patches and the base is what a game on this patch does. A shift is
/// read over the whole window (the served patch and up to <c>MaxPatchLookback</c> before
/// it), against the base share <em>of that same window</em>: how much a magic-heavy team
/// moves an item is far more stable than how often the item is built, and a ratio between
/// two windows would not be a ratio of anything.
/// </para>
/// <para>
/// <b>Candidates are the served patch's.</b> As with the verdicts, an item only the older
/// patches of the window built is not a decision of this patch and gets no row.
/// </para>
/// </remarks>
public static class NextItemTermBuilder
{
    private readonly record struct StatKey(
        ItemContextSlot Slot, int ParentItemId, int ItemId, ItemContextAxis Axis, ItemContextBucket Bucket, string Patch);

    private readonly record struct TotalKey(
        ItemContextSlot Slot, int ParentItemId, ItemContextAxis Axis, ItemContextBucket Bucket, string Patch);

    private readonly record struct Counts(int Games, int Wins)
    {
        public static Counts operator +(Counts left, Counts right) => new(left.Games + right.Games, left.Wins + right.Wins);
    }

    /// <summary>The slots the model serves. Starters are decided before the game opens and the draft already shows them.</summary>
    private static readonly ItemContextSlot[] ServedSlots = [ItemContextSlot.Build, ItemContextSlot.Boots];

    public static IReadOnlyList<ChampionNextItemTerm> Build(
        ItemContextScope scope,
        IReadOnlyList<ChampionItemContextStat> stats,
        IReadOnlyList<ChampionItemContextTotal> totals,
        IReadOnlyList<string> patchWindow,
        NextItemModelOptions options,
        DateTime aggregatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(patchWindow);
        ArgumentNullException.ThrowIfNull(options);

        if (patchWindow.Count == 0)
        {
            return [];
        }

        var servedPatch = patchWindow[0];
        var statsByKey = stats.ToDictionary(
            row => new StatKey(row.Slot, row.ParentItemId, row.ItemId, row.Axis, row.Bucket, row.Patch),
            row => new Counts(row.Games, row.Wins));
        var totalsByKey = totals.ToDictionary(
            row => new TotalKey(row.Slot, row.ParentItemId, row.Axis, row.Bucket, row.Patch),
            row => new Counts(row.Games, row.Wins));

        // Which (axis, bucket) cells each branch has a denominator for, and which axes each
        // item has numerators on — the latter is the whitelist, as the fold applied it.
        var cellsByBranch = totals
            .Where(row => row.Axis != ItemContextAxis.Overall && patchWindow.Contains(row.Patch))
            .GroupBy(row => (row.Slot, row.ParentItemId))
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => (row.Axis, row.Bucket)).Distinct().ToList());
        var axesByItem = stats
            .Where(row => row.Axis != ItemContextAxis.Overall && patchWindow.Contains(row.Patch))
            .GroupBy(row => (row.Slot, row.ParentItemId, row.ItemId))
            .ToDictionary(group => group.Key, group => group.Select(row => row.Axis).ToHashSet());

        var terms = new List<ChampionNextItemTerm>();

        var branches = stats
            .Where(row => row.Patch == servedPatch
                && row.Axis == ItemContextAxis.Overall
                && ServedSlots.Contains(row.Slot))
            .GroupBy(row => (row.Slot, row.ParentItemId));

        foreach (var branch in branches)
        {
            var (slot, parentItemId) = branch.Key;
            var baseWindow = BaseWindow(totalsByKey, slot, parentItemId, patchWindow, options.MinBaseBranchGames);
            var baseBranch = SumTotals(totalsByKey, slot, parentItemId, ItemContextAxis.Overall, ItemContextBucket.All, patchWindow, baseWindow);
            var fullBranch = SumTotals(totalsByKey, slot, parentItemId, ItemContextAxis.Overall, ItemContextBucket.All, patchWindow, patchWindow.Count);

            if (baseBranch.Games <= 0)
            {
                continue;
            }

            foreach (var itemId in branch.Select(row => row.ItemId).Distinct())
            {
                var baseItem = SumStats(statsByKey, slot, parentItemId, itemId, ItemContextAxis.Overall, ItemContextBucket.All, patchWindow, baseWindow);
                var baseShare = baseItem.Games / (double)baseBranch.Games;
                if (baseItem.Games <= 0 || baseShare < options.MinCandidateShare)
                {
                    continue;
                }

                terms.Add(Term(scope, slot, parentItemId, itemId, ItemContextAxis.Overall, ItemContextBucket.All,
                    Math.Log(baseShare), baseItem, baseBranch.Games, baseWindow, aggregatedAtUtc));

                var fullItem = SumStats(statsByKey, slot, parentItemId, itemId, ItemContextAxis.Overall, ItemContextBucket.All, patchWindow, patchWindow.Count);
                if (fullBranch.Games <= 0 || fullItem.Games <= 0
                    || !axesByItem.TryGetValue((slot, parentItemId, itemId), out var eligible)
                    || !cellsByBranch.TryGetValue((slot, parentItemId), out var cells))
                {
                    continue;
                }

                var prior = fullItem.Games / (double)fullBranch.Games;

                foreach (var (axis, bucket) in cells.Where(cell => eligible.Contains(cell.Axis)))
                {
                    var cellBranch = SumTotals(totalsByKey, slot, parentItemId, axis, bucket, patchWindow, patchWindow.Count);
                    if (cellBranch.Games <= 0)
                    {
                        continue;
                    }

                    // An eligible item with no row in a bucket built nothing there: zero
                    // games, which the prior turns into a measured negative shift.
                    var cellItem = SumStats(statsByKey, slot, parentItemId, itemId, axis, bucket, patchWindow, patchWindow.Count);
                    var shrunk = (cellItem.Games + (options.PriorGames * prior)) / (cellBranch.Games + options.PriorGames);
                    var weight = Math.Log(shrunk / prior);

                    if (Math.Abs(weight) < options.MinWeight)
                    {
                        continue;
                    }

                    terms.Add(Term(scope, slot, parentItemId, itemId, axis, bucket,
                        weight, cellItem, cellBranch.Games, patchWindow.Count, aggregatedAtUtc));
                }
            }
        }

        return terms;
    }

    /// <summary>The fewest patches, from the served one back, whose branch holds the base floor — the whole window if none does.</summary>
    private static int BaseWindow(
        IReadOnlyDictionary<TotalKey, Counts> totalsByKey,
        ItemContextSlot slot,
        int parentItemId,
        IReadOnlyList<string> patchWindow,
        int minGames)
    {
        for (var window = 1; window <= patchWindow.Count; window++)
        {
            if (SumTotals(totalsByKey, slot, parentItemId, ItemContextAxis.Overall, ItemContextBucket.All, patchWindow, window).Games >= minGames)
            {
                return window;
            }
        }

        return patchWindow.Count;
    }

    private static ChampionNextItemTerm Term(
        ItemContextScope scope,
        ItemContextSlot slot,
        int parentItemId,
        int itemId,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        double weight,
        Counts item,
        int branchGames,
        int patchWindow,
        DateTime aggregatedAtUtc)
        => new()
        {
            ChampionId = scope.ChampionId,
            Position = scope.Position,
            Patch = scope.Patch,
            Slot = slot,
            ParentItemId = parentItemId,
            ItemId = itemId,
            Axis = axis,
            Bucket = bucket,
            Weight = weight,
            Games = item.Games,
            Wins = item.Wins,
            BranchGames = branchGames,
            PatchWindow = patchWindow,
            AggregatedAtUtc = aggregatedAtUtc,
        };

    private static Counts SumStats(
        IReadOnlyDictionary<StatKey, Counts> statsByKey,
        ItemContextSlot slot,
        int parentItemId,
        int itemId,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        IReadOnlyList<string> patchWindow,
        int window)
    {
        var total = default(Counts);
        for (var i = 0; i < window; i++)
        {
            total += statsByKey.GetValueOrDefault(new StatKey(slot, parentItemId, itemId, axis, bucket, patchWindow[i]));
        }

        return total;
    }

    private static Counts SumTotals(
        IReadOnlyDictionary<TotalKey, Counts> totalsByKey,
        ItemContextSlot slot,
        int parentItemId,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        IReadOnlyList<string> patchWindow,
        int window)
    {
        var total = default(Counts);
        for (var i = 0; i < window; i++)
        {
            total += totalsByKey.GetValueOrDefault(new TotalKey(slot, parentItemId, axis, bucket, patchWindow[i]));
        }

        return total;
    }
}
