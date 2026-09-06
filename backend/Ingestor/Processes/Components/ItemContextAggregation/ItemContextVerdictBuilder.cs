using Core.Lol.ItemContext;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// Turns the item-context counters into the verdicts the page reads (#1450): for each step
/// of each slot — one edge of the build tree — whether the item is <c>Core</c>,
/// <c>Situational</c> or a <c>Preference</c>, and, when situational, the situations that
/// measurably move it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A situation only exists where the build branches (#1496).</b> Every rate here is
/// relative to its branch: the games that reached the parent item and went on to complete
/// something. An item no sibling competes with — nothing else on the branch clears
/// <c>MinAlternativeShare</c> — is <c>Core</c> whatever its rate, because there was no
/// decision to explain and a situation attached to a forced step is a coincidence with a
/// sentence around it. Where two siblings do compete, the axis test is a comparison between
/// them over one cohort, which is the question a reader is actually asking of a branch:
/// <em>this one or that one, and when</em>.
/// </para>
/// <para>
/// <b>The rule, in one place.</b> Below <c>MinPickRate</c> of its branch an item is not a
/// decision worth a card at all. At or above <c>CoreRate</c> it is taken whatever the draft
/// and no situation is looked for. In between, and only on a branch that offers a real
/// alternative, every whitelisted axis is tested by contrasting its two ends — the middle
/// bucket is stored but never compared, so a lift is the gap between "AP-heavy" and "not
/// AP-heavy" rather than a cut through the middle of the distribution. An axis has to clear
/// <em>three</em> floors together: enough games in both buckets, an absolute lift worth
/// reading, and statistical significance. Any one of them alone lies — a large sample makes
/// a two-point gap significant, and a spectacular gap over eleven games is noise.
/// </para>
/// <para>
/// <b>Draft-time findings come first.</b> The feature answers "what do I build against this
/// draft", so an axis known at champion select outranks the one that is not (the gold lead
/// at 15 minutes) whatever their lifts — the strongest finding a reader cannot act on until
/// minute 15 must not take the first line from one they can act on before the game starts.
/// </para>
/// <para>
/// <b>Widening, and what it is honest about.</b> A situation is much rarer than a champion,
/// so a bucket can be thin on a patch the champion itself is well covered on. Rather than
/// drop the axis, the builder folds the previous patches into <em>both</em> ends together
/// (never one, or the two rates would come from different windows) until the floor is met,
/// up to <c>MaxPatchLookback</c>. The window is recorded per finding, because "over the last
/// three patches" is a different claim from "this patch" and the sentence has to say which
/// one it is making. The class and the pick rate are never widened: those describe the
/// served patch alone.
/// </para>
/// <para>
/// <b>Known limit: the lane opponent is not held out.</b> A team-level axis can in principle
/// be carried by one recurring lane opponent rather than by the situation itself. Testing
/// that needs the opponent as a dimension of the counters, which multiplies them by the
/// number of opponents a champion meets (~70 on production) — prohibitive at this grain, so
/// it is deliberately not done here (#1462). What blocks the absurd cases meanwhile is the
/// mechanical whitelist upstream: an item is only ever offered situations it could answer.
/// </para>
/// </remarks>
public static class ItemContextVerdictBuilder
{
    private readonly record struct StatKey(
        ItemContextSlot Slot, int ParentItemId, int ItemId, ItemContextAxis Axis, ItemContextBucket Bucket, string Patch);

    private readonly record struct TotalKey(
        ItemContextSlot Slot, int ParentItemId, ItemContextAxis Axis, ItemContextBucket Bucket, string Patch);

    private readonly record struct BranchKey(ItemContextSlot Slot, int ParentItemId);

    private readonly record struct Counts(int Games, int Wins);

    public static IReadOnlyList<ChampionItemContextVerdict> Build(
        ItemContextScope scope,
        IReadOnlyList<ChampionItemContextStat> stats,
        IReadOnlyList<ChampionItemContextTotal> totals,
        IReadOnlyList<string> patchWindow,
        ItemContextAggregationOptions options,
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

        var maxWindow = Math.Min(options.MaxPatchLookback + 1, patchWindow.Count);
        var verdicts = new List<ChampionItemContextVerdict>();

        // One verdict per step the served patch actually saw. Items that only appear in the
        // older patches of the window are not decisions of this patch and get no row — the
        // window exists to deepen a finding, never to resurrect an item.
        var branches = stats
            .Where(row => row.Patch == servedPatch && row.Axis == ItemContextAxis.Overall)
            .GroupBy(row => new BranchKey(row.Slot, row.ParentItemId));

        foreach (var branch in branches)
        {
            var (slot, parentItemId) = (branch.Key.Slot, branch.Key.ParentItemId);
            var branchGames = totalsByKey
                .GetValueOrDefault(new TotalKey(slot, parentItemId, ItemContextAxis.Overall, ItemContextBucket.All, servedPatch))
                .Games;

            if (branchGames <= 0)
            {
                continue;
            }

            var siblings = branch
                .Select(row => (row.ItemId, Share: row.Games / (double)branchGames, row.Games, row.Wins))
                .ToList();

            foreach (var sibling in siblings)
            {
                if (sibling.Games <= 0 || sibling.Share < options.MinPickRate)
                {
                    continue;
                }

                // The whole point of the branch grain: an item nothing competes with was not
                // chosen over anything, so no situation is looked for and none is printed.
                var hasAlternative = siblings.Any(other =>
                    other.ItemId != sibling.ItemId && other.Share >= options.MinAlternativeShare);
                var isDecision = hasAlternative && sibling.Share < options.CoreRate;

                var findings = isDecision
                    ? BuildFindings(slot, parentItemId, sibling.ItemId, statsByKey, totalsByKey, patchWindow, maxWindow, options)
                    : [];

                verdicts.Add(new ChampionItemContextVerdict
                {
                    ChampionId = scope.ChampionId,
                    Position = scope.Position,
                    Patch = servedPatch,
                    Slot = slot,
                    ParentItemId = parentItemId,
                    ItemId = sibling.ItemId,
                    Games = sibling.Games,
                    Wins = sibling.Wins,
                    BranchGames = branchGames,
                    PickRate = sibling.Share,
                    Class = !isDecision
                        ? ItemContextClass.Core
                        : findings.Count > 0
                            ? ItemContextClass.Situational
                            : ItemContextClass.Preference,
                    PatchWindow = findings.Count > 0 ? findings.Max(finding => finding.PatchWindow) : 1,
                    Axes = findings,
                    AggregatedAtUtc = aggregatedAtUtc,
                });
            }
        }

        return verdicts;
    }

    private static List<ItemContextAxisFinding> BuildFindings(
        ItemContextSlot slot,
        int parentItemId,
        int itemId,
        IReadOnlyDictionary<StatKey, Counts> statsByKey,
        IReadOnlyDictionary<TotalKey, Counts> totalsByKey,
        IReadOnlyList<string> patchWindow,
        int maxWindow,
        ItemContextAggregationOptions options)
    {
        var findings = new List<ItemContextAxisFinding>();

        foreach (var axis in ItemContextAxes.Situational)
        {
            var finding = Judge(slot, parentItemId, itemId, axis, statsByKey, totalsByKey, patchWindow, maxWindow, options);
            if (finding is not null)
            {
                findings.Add(finding);
            }
        }

        return [.. findings
            .OrderByDescending(finding => ItemContextAxes.IsDraftTime(finding.Axis))
            .ThenByDescending(finding => finding.Lift)
            .ThenByDescending(finding => finding.TotalIn + finding.TotalOut)
            .Take(options.MaxAxesPerVerdict)];
    }

    private static ItemContextAxisFinding? Judge(
        ItemContextSlot slot,
        int parentItemId,
        int itemId,
        ItemContextAxis axis,
        IReadOnlyDictionary<StatKey, Counts> statsByKey,
        IReadOnlyDictionary<TotalKey, Counts> totalsByKey,
        IReadOnlyList<string> patchWindow,
        int maxWindow,
        ItemContextAggregationOptions options)
    {
        for (var window = 1; window <= maxWindow; window++)
        {
            var highTotal = SumTotals(totalsByKey, slot, parentItemId, axis, ItemContextBucket.High, patchWindow, window);
            var lowTotal = SumTotals(totalsByKey, slot, parentItemId, axis, ItemContextBucket.Low, patchWindow, window);

            // Both ends widen together or neither does: two rates read off different patch
            // windows are not comparable, and the gap between them would not be a lift.
            if (highTotal < options.MinBucketGames || lowTotal < options.MinBucketGames)
            {
                continue;
            }

            var highGames = SumStats(statsByKey, slot, parentItemId, itemId, axis, ItemContextBucket.High, patchWindow, window);
            var lowGames = SumStats(statsByKey, slot, parentItemId, itemId, axis, ItemContextBucket.Low, patchWindow, window);

            var highRate = highGames / (double)highTotal;
            var lowRate = lowGames / (double)lowTotal;
            var highIsIn = highRate >= lowRate;

            var (bucket, gamesIn, totalIn, gamesOut, totalOut) = highIsIn
                ? (ItemContextBucket.High, highGames, highTotal, lowGames, lowTotal)
                : (ItemContextBucket.Low, lowGames, lowTotal, highGames, highTotal);

            var lift = (gamesIn / (double)totalIn) - (gamesOut / (double)totalOut);
            if (lift < options.MinAbsoluteLift)
            {
                return null;
            }

            var z = ItemContextMath.TwoProportionZ(gamesIn, totalIn, gamesOut, totalOut);
            if (Math.Abs(z) < options.MinAbsoluteZ)
            {
                return null;
            }

            return new ItemContextAxisFinding
            {
                Axis = axis,
                Bucket = bucket,
                GamesIn = gamesIn,
                TotalIn = totalIn,
                GamesOut = gamesOut,
                TotalOut = totalOut,
                Lift = lift,
                Z = z,
                PatchWindow = window,
            };
        }

        return null;
    }

    private static int SumStats(
        IReadOnlyDictionary<StatKey, Counts> statsByKey,
        ItemContextSlot slot,
        int parentItemId,
        int itemId,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        IReadOnlyList<string> patchWindow,
        int window)
    {
        var total = 0;
        for (var i = 0; i < window; i++)
        {
            total += statsByKey
                .GetValueOrDefault(new StatKey(slot, parentItemId, itemId, axis, bucket, patchWindow[i]))
                .Games;
        }

        return total;
    }

    private static int SumTotals(
        IReadOnlyDictionary<TotalKey, Counts> totalsByKey,
        ItemContextSlot slot,
        int parentItemId,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        IReadOnlyList<string> patchWindow,
        int window)
    {
        var total = 0;
        for (var i = 0; i < window; i++)
        {
            total += totalsByKey
                .GetValueOrDefault(new TotalKey(slot, parentItemId, axis, bucket, patchWindow[i]))
                .Games;
        }

        return total;
    }
}
