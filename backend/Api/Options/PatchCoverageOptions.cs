namespace TrueMain.Options;

/// <summary>
/// Sizes and the one judgement call behind the admin patch-coverage view (#1033),
/// bound from <c>PatchCoverage:*</c>.
///
/// <para>
/// The games floor itself is deliberately <b>not</b> here: it is
/// <c>ChampionsList:MinSampleGames</c>, read straight from
/// <see cref="ChampionsListOptions"/>, because the whole point of the page is to
/// answer "is what the public reads servable" — a second, independently
/// configurable floor could drift from the real one and the page would then
/// confidently report on a bar nobody enforces.
/// </para>
/// </summary>
public sealed class PatchCoverageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "PatchCoverage";

    /// <summary>
    /// How many patches the view covers, newest first — the current one plus the few
    /// before it. Older patches are frozen by design (#466), so listing more of them
    /// adds rows nothing can act on; the ones just behind the current patch are what a
    /// reader compares against.
    /// </summary>
    public int PatchCount { get; set; } = 4;

    /// <summary>
    /// How many below-floor <c>(champion, lane)</c> lines to name per patch. A display
    /// cap: the full count travels alongside, so a truncated list still says how much it
    /// is hiding. The whole roster across five lanes is under a thousand lines, so this
    /// bounds the payload rather than the query.
    /// </summary>
    public int ThinLineLimit { get; set; } = 40;

    /// <summary>
    /// Share of the comparable patches' median that a patch's past-the-floor line count
    /// must reach to read as servable.
    ///
    /// <para>
    /// A ratio rather than an absolute count because the honest bar moves with the corpus:
    /// the number of lines that clear ten games grows as tracked accounts are added, so a
    /// hard-coded "300 lines" would go permanently green on a healthy database and
    /// permanently red on preprod. The reference is the median of the covered patches
    /// <em>excluding the newest</em> — the same "the edge patch is still filling, so it is
    /// not comparable" rule the patch-volume detector already applies (#924).
    /// </para>
    /// </summary>
    public double ServableLinesRatio { get; set; } = 0.6;

    /// <summary>
    /// The fallback bar when there is no comparable patch to take a median from — a
    /// database holding a single patch, which is preprod's normal state. Absolute and
    /// therefore crude, which is why it is only ever the fallback: it exists so the
    /// verdict is still an answer rather than a shrug.
    /// </summary>
    public long ServableLinesMinimum { get; set; } = 100;
}
