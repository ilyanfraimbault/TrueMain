using Core.Lol.ItemContext;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// Turns the item-context counters into the verdicts the page reads (#1450): for each item
/// of each slot, whether it is <c>Core</c>, <c>Situational</c> or a <c>Preference</c>, and
/// — when situational — the situations that measurably move it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule, in one place.</b> An item's unconditional pick rate decides its class: at
/// or above <c>CoreRate</c> it is built whatever the draft and no situation is looked for;
/// below <c>MinPickRate</c> it is not a decision worth a card at all. In between, every
/// whitelisted axis is tested by contrasting its two ends — the middle bucket is stored but
/// never compared, so a lift is the gap between "AP-heavy" and "not AP-heavy" rather than a
/// cut through the middle of the distribution. An axis has to clear <em>three</em> floors
/// together: enough games in both buckets, an absolute lift worth reading, and statistical
/// significance. Any one of them alone lies — a large sample makes a two-point gap
/// significant, and a spectacular gap over eleven games is noise.
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
    private readonly record struct StatKey(ItemContextSlot Slot, int ItemId, ItemContextAxis Axis, ItemContextBucket Bucket, string Patch);

    private readonly record struct TotalKey(ItemContextSlot Slot, ItemContextAxis Axis, ItemContextBucket Bucket, string Patch);

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
            row => new StatKey(row.Slot, row.ItemId, row.Axis, row.Bucket, row.Patch),
            row => new Counts(row.Games, row.Wins));
        var totalsByKey = totals.ToDictionary(
            row => new TotalKey(row.Slot, row.Axis, row.Bucket, row.Patch),
            row => new Counts(row.Games, row.Wins));

        var maxWindow = Math.Min(options.MaxPatchLookback + 1, patchWindow.Count);
        var verdicts = new List<ChampionItemContextVerdict>();

        // One verdict per (slot, item) the served patch actually saw. Items that only
        // appear in the older patches of the window are not decisions of this patch and get
        // no row — the window exists to deepen a finding, never to resurrect an item.
        var served = stats
            .Where(row => row.Patch == servedPatch && row.Axis == ItemContextAxis.Overall)
            .Select(row => (row.Slot, row.ItemId))
            .Distinct();

        foreach (var (slot, itemId) in served)
        {
            var itemGames = statsByKey
                .GetValueOrDefault(new StatKey(slot, itemId, ItemContextAxis.Overall, ItemContextBucket.All, servedPatch));
            var slotGames = totalsByKey
                .GetValueOrDefault(new TotalKey(slot, ItemContextAxis.Overall, ItemContextBucket.All, servedPatch));

            if (slotGames.Games <= 0 || itemGames.Games <= 0)
            {
                continue;
            }

            var pickRate = itemGames.Games / (double)slotGames.Games;
            if (pickRate < options.MinPickRate)
            {
                continue;
            }

            var findings = pickRate >= options.CoreRate
                ? []
                : BuildFindings(slot, itemId, statsByKey, totalsByKey, patchWindow, maxWindow, options);

            verdicts.Add(new ChampionItemContextVerdict
            {
                ChampionId = scope.ChampionId,
                Position = scope.Position,
                Patch = servedPatch,
                Slot = slot,
                ItemId = itemId,
                Games = itemGames.Games,
                Wins = itemGames.Wins,
                SlotGames = slotGames.Games,
                PickRate = pickRate,
                Class = pickRate >= options.CoreRate
                    ? ItemContextClass.Core
                    : findings.Count > 0
                        ? ItemContextClass.Situational
                        : ItemContextClass.Preference,
                PatchWindow = findings.Count > 0 ? findings.Max(finding => finding.PatchWindow) : 1,
                Axes = findings,
                AggregatedAtUtc = aggregatedAtUtc,
            });
        }

        return verdicts;
    }

    private static List<ItemContextAxisFinding> BuildFindings(
        ItemContextSlot slot,
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
            var finding = Judge(slot, itemId, axis, statsByKey, totalsByKey, patchWindow, maxWindow, options);
            if (finding is not null)
            {
                findings.Add(finding);
            }
        }

        return [.. findings
            .OrderByDescending(finding => finding.Lift)
            .ThenByDescending(finding => finding.TotalIn + finding.TotalOut)
            .Take(options.MaxAxesPerVerdict)];
    }

    private static ItemContextAxisFinding? Judge(
        ItemContextSlot slot,
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
            var highTotal = SumTotals(totalsByKey, slot, axis, ItemContextBucket.High, patchWindow, window);
            var lowTotal = SumTotals(totalsByKey, slot, axis, ItemContextBucket.Low, patchWindow, window);

            // Both ends widen together or neither does: two rates read off different patch
            // windows are not comparable, and the gap between them would not be a lift.
            if (highTotal < options.MinBucketGames || lowTotal < options.MinBucketGames)
            {
                continue;
            }

            var highGames = SumStats(statsByKey, slot, itemId, axis, ItemContextBucket.High, patchWindow, window);
            var lowGames = SumStats(statsByKey, slot, itemId, axis, ItemContextBucket.Low, patchWindow, window);

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
        int itemId,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        IReadOnlyList<string> patchWindow,
        int window)
    {
        var total = 0;
        for (var i = 0; i < window; i++)
        {
            total += statsByKey.GetValueOrDefault(new StatKey(slot, itemId, axis, bucket, patchWindow[i])).Games;
        }

        return total;
    }

    private static int SumTotals(
        IReadOnlyDictionary<TotalKey, Counts> totalsByKey,
        ItemContextSlot slot,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        IReadOnlyList<string> patchWindow,
        int window)
    {
        var total = 0;
        for (var i = 0; i < window; i++)
        {
            total += totalsByKey.GetValueOrDefault(new TotalKey(slot, axis, bucket, patchWindow[i])).Games;
        }

        return total;
    }
}
