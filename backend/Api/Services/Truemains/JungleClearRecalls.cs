using Data.Entities;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Derives a jungler's mid-clear base visits from their stored item events
/// (#1186). <c>jungle_first_clears</c> deliberately persists only the camp
/// sequence — the builder skips recall minutes because jungle CS does not
/// advance — so backs are reconstructed at read time: a shop purchase strictly
/// between the first and last clear step is a base visit. Purchases are the
/// signal because junglers' timelines carry spurious <c>ITEM_DESTROYED</c>
/// events for smite-charge/consumable artifacts (see decisions.md, the
/// starter-item scan rule): only <c>ITEM_PURCHASED</c> proves a shop trip.
///
/// Accepted imprecision: a death followed by a buy also reads as a base visit
/// (honest either way — the clear was interrupted), and step timestamps are
/// minute-resolution, so which gap a recall lands in is approximate.
/// </summary>
internal static class JungleClearRecalls
{
    /// <summary>
    /// Purchases earlier than this are the start-of-game shopping trip, not a
    /// mid-clear back, even when the first detected camp step is earlier still.
    /// </summary>
    internal const int StartOfGameShoppingCutoffMs = 60_000;

    /// <summary>
    /// Purchases closer together than this belong to the same shop visit — a
    /// player buys an item, a control ward and a refillable in one back.
    /// </summary>
    internal const int SameVisitWindowMs = 30_000;

    public static List<MatchDetailJungleClearRecallReadModel> Derive(
        IReadOnlyList<JungleClearStep> steps,
        IReadOnlyList<ItemEvent> itemEvents)
    {
        var recalls = new List<MatchDetailJungleClearRecallReadModel>();
        if (steps.Count < 2)
        {
            return recalls;
        }

        var windowStart = Math.Max(StartOfGameShoppingCutoffMs, steps[0].TimestampMs);
        var windowEnd = steps[^1].TimestampMs;

        var purchases = itemEvents
            .Where(e => e.EventType == "ITEM_PURCHASED"
                && e.TimestampMs > windowStart
                && e.TimestampMs < windowEnd)
            .Select(e => e.TimestampMs)
            .OrderBy(t => t)
            .ToList();

        // Cluster purchases into shop visits, then keep one visit per step gap
        // (the earliest): the display draws at most one fountain detour between
        // two camps, and a second cluster in the same gap is almost always the
        // same interruption seen twice.
        var coveredGaps = new HashSet<int>();
        int? lastPurchaseMs = null;
        foreach (var timestamp in purchases)
        {
            var isNewVisit = lastPurchaseMs is null
                || timestamp - lastPurchaseMs.Value > SameVisitWindowMs;
            lastPurchaseMs = timestamp;
            if (!isNewVisit)
            {
                continue;
            }

            var afterStepIndex = AfterStepIndexOf(steps, timestamp);
            if (!coveredGaps.Add(afterStepIndex))
            {
                continue;
            }

            recalls.Add(new MatchDetailJungleClearRecallReadModel
            {
                TimestampMs = timestamp,
                AfterStepIndex = afterStepIndex,
            });
        }

        return recalls;
    }

    /// <summary>
    /// The largest step index whose timestamp is ≤ <paramref name="timestamp"/>.
    /// The strict purchase window guarantees a bracketing gap exists, so the
    /// result is always in <c>[0, steps.Count - 2]</c>.
    /// </summary>
    private static int AfterStepIndexOf(IReadOnlyList<JungleClearStep> steps, int timestamp)
    {
        for (var i = steps.Count - 2; i >= 1; i--)
        {
            if (steps[i].TimestampMs <= timestamp)
            {
                return i;
            }
        }

        return 0;
    }
}
