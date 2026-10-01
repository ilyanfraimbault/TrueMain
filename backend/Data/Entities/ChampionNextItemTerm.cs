using Data.ItemContext;

namespace Data.Entities;

/// <summary>
/// One term of the next-item model (#1749): for one step of one build — an edge of the
/// build tree — either the step's base log-share on its branch, or how much one draft
/// situation shifts that share. The desktop app's in-game panel asks "what do I complete
/// next" on every purchase, so the answer has to be a lookup and a sum, never a scan of
/// matches; this table is that lookup.
///
/// <para>
/// <b>Derived, like the verdicts.</b> Rebuilt wholesale from
/// <see cref="ChampionItemContextStat"/> and <see cref="ChampionItemContextTotal"/> at the
/// end of the item-context fold, for the scopes the run touched. A re-tuned prior costs a
/// rebuild, never a re-fold.
/// </para>
///
/// <para>
/// <b>Reading it.</b> The row on <see cref="ItemContextAxis.Overall"/> /
/// <see cref="ItemContextBucket.All"/> carries <c>ln(share of the branch)</c>. Every other
/// row carries <c>ln(share in that bucket / share overall)</c>, shrunk towards zero by the
/// bucket's sample. A game's score for an item is its base term plus the terms of the
/// buckets the game sits in (<see cref="NextItemScorer"/>); a pair with no row contributes
/// nothing — either the item cannot mechanically answer that situation
/// (<see cref="ItemContextWhitelist"/>) or the shift was too small to keep.
/// </para>
/// </summary>
public class ChampionNextItemTerm
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Canonical <c>TeamPosition</c>.</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>The patch this model is served for.</summary>
    public string Patch { get; set; } = string.Empty;

    public ItemContextSlot Slot { get; set; }

    /// <summary>The item the decision follows, 0 for the branch every game starts on.</summary>
    public int ParentItemId { get; set; }

    public int ItemId { get; set; }

    public ItemContextAxis Axis { get; set; }

    public ItemContextBucket Bucket { get; set; }

    /// <summary>The natural-log term: a base log-share on <c>Overall</c>, a log-ratio elsewhere.</summary>
    public double Weight { get; set; }

    /// <summary>Games in the term's cohort that took this step.</summary>
    public int Games { get; set; }

    public int Wins { get; set; }

    /// <summary>Games of the branch in the term's cohort — the denominator of the share.</summary>
    public int BranchGames { get; set; }

    /// <summary>How many patches the term was measured over, the served one included.</summary>
    public int PatchWindow { get; set; } = 1;

    public DateTime AggregatedAtUtc { get; set; }
}
