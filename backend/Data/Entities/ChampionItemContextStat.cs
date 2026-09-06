using Data.ItemContext;

namespace Data.Entities;

/// <summary>
/// How often a champion built one item, in games sitting at one end of one draft axis
/// (#1450) — the numerator of every situational sentence. Its denominator lives in
/// <see cref="ChampionItemContextTotal"/> on exactly the same grain minus the item, and
/// the same fold writes both from the same games, so a rate is always computed over one
/// cohort. The ban aggregate pair (#920) is the precedent, and for the same reason: the
/// denominator here is "games in this situation", which no read can recount once the
/// matches are retired.
///
/// <para>
/// <b>The synthetic axis.</b> <see cref="ItemContextAxis.Overall"/> paired with
/// <see cref="ItemContextBucket.All"/> carries the item's unconditional counts, so the
/// pick rate that decides Core / Situational / Preference and the conditional rates that
/// explain it come out of one table over one set of games rather than two reads that can
/// disagree.
/// </para>
///
/// <para>
/// <b>Only whitelisted pairs exist.</b> A row is written for an (item, axis) pair only
/// when the item could mechanically answer that situation
/// (<see cref="ItemContextWhitelist"/>). This is what keeps the table at the scale of the
/// matchup pre-aggregation instead of fifteen axes times every item, and it is the same
/// rule the read applies — counting a pair we would never show would only buy the option
/// of showing a coincidence later.
/// </para>
///
/// <para>
/// <b>No elo dimension.</b> Unlike the matchup and ban aggregates this one is not split by
/// rank: a situation is far rarer than a champion, and splitting the games eleven ways
/// starves exactly the buckets the whole feature depends on. The verdicts say so, and the
/// card says so.
/// </para>
/// </summary>
public class ChampionItemContextStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Canonical <c>TeamPosition</c>.</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Which decision this row is about: a build item, the boots, or a starter.</summary>
    public ItemContextSlot Slot { get; set; }

    /// <summary>
    /// The item this decision was made <em>after</em> — the branch of the build tree this
    /// row belongs to (#1496). 0 is the branch every game starts on: the first legendary,
    /// the boots and the starter basket are all decided before any completed item exists.
    /// </summary>
    /// <remarks>
    /// This is what makes a rate here the answer to "of the games that reached this item,
    /// how many went on to that one" rather than "of all games, how many ever finished that
    /// one". The second question cannot be asked of a draft: an item late in the build is
    /// completed mostly in the games that lasted long enough to buy it, which is why the
    /// flat grain this replaced explained nine findings in ten by the player's own gold
    /// lead at 15 minutes.
    /// </remarks>
    public int ParentItemId { get; set; }

    public int ItemId { get; set; }

    public ItemContextAxis Axis { get; set; }

    public ItemContextBucket Bucket { get; set; }

    /// <summary>Games in this bucket where the champion built the item.</summary>
    public int Games { get; set; }

    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
