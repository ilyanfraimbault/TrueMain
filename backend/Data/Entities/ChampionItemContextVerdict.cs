using Data.ItemContext;

namespace Data.Entities;

/// <summary>
/// The answer the page reads (#1450): for one step of one build — one edge of the build
/// tree — whether the item is core, situational or a preference, and — when situational —
/// the situations that measurably move it, each with its rates and its sample.
///
/// <para>
/// <b>One branch, one decision (#1496).</b> A verdict is scoped to the games that reached
/// <see cref="ParentItemId"/> and went on to complete something, so the siblings of a branch
/// share one denominator and a class is a statement about a <em>choice</em>. A branch whose
/// games only ever continue into one item has no choice in it, and every item on it is
/// <see cref="ItemContextClass.Core"/> whatever its rate: an item with no alternative cannot
/// be explained by a situation, and saying otherwise is what the flat grain used to do.
/// </para>
///
/// <para>
/// <b>Derived, and rebuilt rather than accumulated.</b> Everything here is computed from
/// <see cref="ChampionItemContextStat"/> and <see cref="ChampionItemContextTotal"/> at the
/// end of a fold, for the scopes that fold touched, and replaced wholesale. That is what
/// makes the read a lookup with no statistics in it — the requirement this table exists
/// for — and it means a threshold can be re-tuned by recomputing the verdicts alone,
/// without re-folding a single match.
/// </para>
/// </summary>
public class ChampionItemContextVerdict
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    public string Position { get; set; } = string.Empty;

    /// <summary>The patch this verdict is served for — not necessarily the only patch behind it, see <see cref="PatchWindow"/>.</summary>
    public string Patch { get; set; } = string.Empty;

    public ItemContextSlot Slot { get; set; }

    /// <summary>
    /// The branch of the build tree this verdict answers for (#1496): the item the decision
    /// was made after, 0 for the branch every game starts on. A verdict is about an
    /// <em>edge</em>, never about an item on its own — the same item is a different decision
    /// after a different parent, and a reader hovering it in the tree is asking about the
    /// step it is looking at.
    /// </summary>
    public int ParentItemId { get; set; }

    public int ItemId { get; set; }

    /// <summary>Games on the served patch that took this branch to this item.</summary>
    public int Games { get; set; }

    public int Wins { get; set; }

    /// <summary>
    /// Games of the branch on the served patch — the games that reached the parent and
    /// completed a further item, i.e. the denominator of <see cref="PickRate"/>.
    /// </summary>
    public int BranchGames { get; set; }

    /// <summary>Share of the branch's games that continued into this item, on the served patch alone.</summary>
    public double PickRate { get; set; }

    public ItemContextClass Class { get; set; }

    /// <summary>
    /// How many patches the axis findings were folded over: 1 when the served patch was
    /// deep enough on its own, more when a thin bucket made the builder widen backwards.
    /// The sentence prints it, because "over the last 3 patches" is a different claim from
    /// "this patch".
    /// </summary>
    public int PatchWindow { get; set; } = 1;

    /// <summary>
    /// The qualifying situations, strongest lift first, as <c>jsonb</c>. Empty for
    /// <see cref="ItemContextClass.Core"/> and <see cref="ItemContextClass.Preference"/> —
    /// those two classes are themselves the whole answer.
    /// </summary>
    public List<ItemContextAxisFinding> Axes { get; set; } = [];

    public DateTime AggregatedAtUtc { get; set; }
}
