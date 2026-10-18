using Data.Entities;

namespace Data.BuildFacts;

/// <summary>
/// The step of the build order a game's boots took (#2101): 1 when they were bought before
/// the first legendary, 2 right after it, and so on. The in-game panel shows the build as one
/// row and has to say where the boots go in it — "after your first item" is the question a
/// player actually asks, and which boots is a separate one (<see cref="BootsResolver"/>).
/// </summary>
public static class BootsStepResolver
{
    /// <summary>The step the boots took, from the timeline's purchases.</summary>
    /// <param name="itemEvents">The participant's item timeline.</param>
    /// <param name="buildItems">The completed legendaries, in completion order (<see cref="FinalBuildResolver"/>).</param>
    /// <param name="bootsItemId">The boots the game settled on (<see cref="BootsResolver"/>), 0 for none.</param>
    /// <returns>The step, or 0 when the timeline never shows the boots being bought.</returns>
    public static int Resolve(IReadOnlyList<ItemEvent> itemEvents, IReadOnlyList<int> buildItems, int bootsItemId)
    {
        ArgumentNullException.ThrowIfNull(itemEvents);
        ArgumentNullException.ThrowIfNull(buildItems);

        if (bootsItemId <= 0)
        {
            return 0;
        }

        var bootsAt = FirstPurchase(itemEvents, bootsItemId);
        if (bootsAt is null)
        {
            // Boots read off the final inventory only: when they came is not known, and a
            // guess would be counted as a measured step.
            return 0;
        }

        // A legendary with no purchase on the timeline (a transform, an item granted
        // whole) is not known to precede the boots, so it does not move them later.
        var before = buildItems.Count(itemId => FirstPurchase(itemEvents, itemId) is { } at && at < bootsAt);
        return before + 1;
    }

    /// <summary>The first purchase of an item that was not undone right after.</summary>
    private static int? FirstPurchase(IReadOnlyList<ItemEvent> itemEvents, int itemId)
    {
        int? first = null;

        foreach (var itemEvent in itemEvents.OrderBy(itemEvent => itemEvent.TimestampMs))
        {
            switch (ItemEventTypes.Classify(itemEvent.EventType))
            {
                case ItemEventKind.Purchased when itemEvent.ItemId == itemId:
                    first ??= itemEvent.TimestampMs;
                    break;
                case ItemEventKind.Undo when (itemEvent.BeforeId ?? itemEvent.ItemId) == itemId:
                    first = null;
                    break;
            }
        }

        return first;
    }
}
