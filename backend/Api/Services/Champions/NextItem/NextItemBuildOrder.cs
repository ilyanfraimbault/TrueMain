using Data.ItemContext;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.NextItem;

/// <summary>
/// The build as one row (#2101): the legendaries and the boots already completed, the step
/// to complete next, and the likely rest of the build — so the panel can say where the game
/// stands in it, and when the boots come.
/// </summary>
/// <remarks>
/// <para>
/// <b>The continuation is the tree walked forward</b>: from the next item, each branch's
/// best candidate for this game's situation, until the build holds
/// <see cref="MaxLegendaries"/> legendaries or the tree ends. It is the mains' most likely
/// path from here, not a promise — every purchase asks again.
/// </para>
/// <para>
/// <b>The boots sit at the step the mains complete them at</b>
/// (<see cref="NextItemModel.BootsStep"/>). A game already past that step without boots has
/// them as its next step: being late on boots is exactly what the row exists to say. Once a
/// pair is held it stays where it was bought.
/// </para>
/// </remarks>
public static class NextItemBuildOrder
{
    /// <summary>A full build: five legendaries beside the boots.</summary>
    public const int MaxLegendaries = 5;

    /// <summary>Games below which when the boots come is not said at all.</summary>
    public const int MinBootsTimingGames = 20;

    public static IReadOnlyList<NextItemOrderStepReadModel> Resolve(
        NextItemModel model,
        IReadOnlyList<int> items,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> situation,
        NextItemSlotReadModel? build,
        NextItemSlotReadModel? boots)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(situation);

        var completed = Completed(model, items);
        var completedLegendaries = completed.Count(step => !step.IsBoots);

        var planned = new List<NextItemOrderStepReadModel>();
        if (build?.Candidates.FirstOrDefault() is { } next)
        {
            var taken = items.Where(item => item > 0).ToHashSet();
            var itemId = next.ItemId;
            while (completedLegendaries + planned.Count < MaxLegendaries)
            {
                planned.Add(Step(itemId, isBoots: false, NextItemOrderState.Upcoming));
                taken.Add(itemId);

                var branch = model.Branch(ItemContextSlot.Build, itemId);
                var following = branch is null ? null : NextItemScorer.Score(branch, situation, taken).FirstOrDefault();
                if (following is null)
                {
                    break;
                }

                itemId = following.ItemId;
            }
        }

        if (boots?.Candidates.FirstOrDefault() is { } pair && model.BootsStep(MinBootsTimingGames) is { } bootsStep)
        {
            // Boots are due after (step - 1) legendaries; a game already past that buys them now.
            var at = Math.Clamp(bootsStep - 1 - completedLegendaries, 0, planned.Count);
            planned.Insert(at, Step(pair.ItemId, isBoots: true, NextItemOrderState.Upcoming));
        }

        if (planned.Count > 0)
        {
            planned[0] = planned[0] with { State = NextItemOrderState.Next };
        }

        return [.. completed, .. planned];
    }

    /// <summary>The legendaries and the boots the game holds, in the order it completed them.</summary>
    private static List<NextItemOrderStepReadModel> Completed(NextItemModel model, IReadOnlyList<int> items)
    {
        var completed = new List<NextItemOrderStepReadModel>();
        foreach (var itemId in items.Where(item => item > 0).Distinct())
        {
            if (model.IsBuildItem(itemId))
            {
                completed.Add(Step(itemId, isBoots: false, NextItemOrderState.Completed));
            }
            else if (model.IsBoots(itemId) && !completed.Any(step => step.IsBoots))
            {
                completed.Add(Step(itemId, isBoots: true, NextItemOrderState.Completed));
            }
        }

        return completed;
    }

    private static NextItemOrderStepReadModel Step(int itemId, bool isBoots, string state)
        => new() { ItemId = itemId, IsBoots = isBoots, State = state };
}
