using Data.ItemContext;

namespace TrueMain.Services.Champions.NextItem;

/// <summary>
/// The advice is a need, not an item (#2371). A player who completes an item the mains
/// rarely take there has still answered whatever situational need that item meets — magic
/// resistance, armour, anti-heal, penetration, tenacity — so the next suggestion must not
/// ask for that need again: buying one magic-resist item instead of the suggested one closes
/// the magic-resist need.
/// </summary>
public static class NextItemNeedCoverage
{
    /// <summary>The needs the off-advice legendaries cover; components and boots cover none.</summary>
    public static ItemNeed Covered(IEnumerable<int> offAdviceItems, ItemNeedCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(offAdviceItems);
        ArgumentNullException.ThrowIfNull(catalog);

        var covered = ItemNeed.None;
        foreach (var itemId in offAdviceItems)
        {
            if (catalog.Covering.Contains(itemId))
            {
                covered |= catalog.Needs.GetValueOrDefault(itemId);
            }
        }

        return covered;
    }

    /// <summary>
    /// What a branch must not suggest: the items already held, plus every candidate answering a
    /// need already covered.
    /// </summary>
    public static IReadOnlySet<int> Excluded(
        NextItemBranch branch,
        IReadOnlySet<int> owned,
        ItemNeed covered,
        ItemNeedCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(catalog);

        if (covered == ItemNeed.None)
        {
            return owned;
        }

        var excluded = new HashSet<int>(owned);
        foreach (var candidate in branch.Candidates)
        {
            if ((catalog.Needs.GetValueOrDefault(candidate.ItemId) & covered) != ItemNeed.None)
            {
                excluded.Add(candidate.ItemId);
            }
        }

        return excluded;
    }
}
