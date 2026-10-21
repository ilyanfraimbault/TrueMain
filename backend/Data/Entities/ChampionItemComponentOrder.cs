namespace Data.Entities;

/// <summary>
/// The order a champion's mains buy one item's components in (#2183): for one recipe slot of
/// one item, in how many games it was the component bought first, second, and so on. The
/// desktop app's build path reads it to say which component to buy now, in the mains' order
/// rather than the cheapest first.
///
/// <para>
/// <b>Folded beside the item context</b>, in the same pass over the same item timelines
/// (<see cref="Match.ItemContextAggregated"/>), additively per match, by
/// <c>BuildFacts.ComponentOrderResolver</c>. An item a game bought outright says nothing
/// about the order and adds no row. Every game the item is counted for ranks every slot of
/// its recipe, so the games of an item are the sum over ranks of any one of its slots.
/// </para>
///
/// <para>
/// Intermediates are items too: Lost Chapter's own components get rows under its id, folded
/// from the games where it went into a completed item.
/// </para>
/// </summary>
public class ChampionItemComponentOrder
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Canonical <c>TeamPosition</c>.</summary>
    public string Position { get; set; } = string.Empty;

    public string Patch { get; set; } = string.Empty;

    /// <summary>The completed item — a legendary, boots or an intermediate of one.</summary>
    public int ItemId { get; set; }

    /// <summary>The component of its recipe.</summary>
    public int ComponentId { get; set; }

    /// <summary>0 for the first time the recipe lists the component, 1 for a second copy.</summary>
    public int Occurrence { get; set; }

    /// <summary>Where the component came among the item's components, 1 for the first.</summary>
    public int Rank { get; set; }

    public int Games { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
