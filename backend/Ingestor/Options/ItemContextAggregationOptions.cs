using Data.ItemContext;

namespace Ingestor.Options;

/// <summary>
/// Batch sizing, floors and verdict rules for <c>ChampionItemContextAggregationProcess</c>
/// (#1450) — the fold behind the situational build context.
/// </summary>
public class ItemContextAggregationOptions
{
    public const string SectionName = "ItemContextAggregation";

    /// <summary>Pending matches folded per transaction. Each one loads ten slim participant rows plus the item timelines of its cohort members.</summary>
    public int MatchBatchSize { get; set; } = 300;

    /// <summary>Upper bound on matches folded in one run; 0 means no cap.</summary>
    public int MaxMatchesPerRun { get; set; } = 20000;

    /// <summary>The bands each draft axis is cut into.</summary>
    public DraftAxisThresholds Axes { get; set; } = new();

    /// <summary>
    /// Games a champion profile must hold before a draft may be qualified against it.
    /// Below this the classification is noise, and a wrong classification does not make a
    /// weaker axis — it makes a wrong one.
    /// </summary>
    /// <remarks>
    /// Measured rather than guessed. On production, patch 16.16 held 858
    /// <c>(champion, position)</c> lines, of which 544 clear 100 games and 459 clear 200 —
    /// and the lines below the floor are overwhelmingly off-role noise a champion's own
    /// fallback position covers anyway. On preprod, whose whole corpus is ~140x smaller,
    /// only 34 of 1 002 lines clear 100 and 7 clear 200: a floor of 200 leaves the feature
    /// dark on the environment ingestor changes are verified on. 100 is where both hold —
    /// a damage share, a sustain rate or a CC rate is stable well before it, since the
    /// floor's real job is excluding the three-game line, not sharpening a mean.
    /// </remarks>
    public int MinProfileGames { get; set; } = 100;

    /// <summary>
    /// How many patches back the profile snapshot may reach for a champion the served
    /// patch does not cover yet. Profiles fill over a patch, so on patch day every draft
    /// would otherwise be unqualifiable.
    /// </summary>
    public int ProfileLookbackPatches { get; set; } = 2;

    /// <summary>
    /// Pick rate at or above which an item is <c>Core</c>: built whatever the draft, so no
    /// situation explains it and none is looked for.
    /// </summary>
    public double CoreRate { get; set; } = 0.85;

    /// <summary>
    /// Pick rate below which an item gets no verdict at all. A build the slice barely ever
    /// makes is not a decision worth explaining, and this is what keeps the served table
    /// an order of magnitude smaller than the counters behind it.
    /// </summary>
    public double MinPickRate { get; set; } = 0.05;

    /// <summary>
    /// Games each of the two compared buckets must hold before an axis may be judged. The
    /// patch window widens (see <see cref="MaxPatchLookback"/>) before an axis is dropped
    /// for missing it.
    /// </summary>
    public int MinBucketGames { get; set; } = 100;

    /// <summary>
    /// Percentage points the two ends must differ by, on top of being statistically
    /// significant. A large sample makes a 2-point gap significant; a 2-point gap is not an
    /// explanation, and this floor — not the p-value — is what keeps the sentences worth
    /// reading.
    /// </summary>
    public double MinAbsoluteLift { get; set; } = 0.10;

    /// <summary>|z| of the two-proportion test an axis must clear. 1.96 is the 95% two-sided edge.</summary>
    public double MinAbsoluteZ { get; set; } = 1.96;

    /// <summary>
    /// How many patches before the served one the builder may fold into a thin axis. A
    /// situation is much rarer than a champion — a bucket can be thin on a patch the
    /// champion itself is well covered on — so widening backwards is what stops the floors
    /// from starving exactly the axes that matter most. The window used is recorded on the
    /// verdict and printed by the card; 0 disables widening.
    /// </summary>
    public int MaxPatchLookback { get; set; } = 2;

    /// <summary>Most findings kept on one verdict, strongest lift first. Three is what a hover card can carry without becoming a table.</summary>
    public int MaxAxesPerVerdict { get; set; } = 3;
}
