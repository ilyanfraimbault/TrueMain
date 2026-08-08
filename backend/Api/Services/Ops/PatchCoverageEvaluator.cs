namespace TrueMain.Services.Ops;

/// <summary>
/// The judgement half of the patch-coverage view (#1033): everything that turns measured
/// counts into "is this patch servable", with no database and no clock of its own.
///
/// <para>
/// Kept pure and separate for the same reason <see cref="DataQualityDetectorEvaluator"/>
/// is — the bar is the part that will be argued about, so it has to be testable without a
/// Postgres container.
/// </para>
/// </summary>
internal static class PatchCoverageEvaluator
{
    /// <summary>
    /// The bar a patch's past-the-floor line count must reach, and the sentence explaining
    /// where that bar came from.
    ///
    /// <para>
    /// A ratio of the median of the <paramref name="settledLinesPastFloor"/> reference
    /// rather than an absolute count, because the honest bar moves with the corpus: the
    /// number of lines clearing ten games grows as tracked accounts are added, so a
    /// hard-coded count goes permanently green on a large database and permanently red on
    /// a small one. With no reference at all the configured minimum applies instead — a
    /// crude answer, but an answer.
    /// </para>
    /// </summary>
    public static PatchCoverageBar ReadBar(
        IReadOnlyList<long> settledLinesPastFloor,
        double ratio,
        long minimum,
        string? servedPatch)
    {
        ArgumentNullException.ThrowIfNull(settledLinesPastFloor);

        var median = Median(settledLinesPastFloor);
        if (median is not { } reference)
        {
            return new PatchCoverageBar(
                minimum,
                $"No settled patch to compare against, so the configured floor of {minimum} lines applies instead.",
                Reference: null,
                ReferencePatches: 0);
        }

        var patchLabel = servedPatch is null ? "the patch being served" : servedPatch;

        return new PatchCoverageBar(
            reference * Math.Max(0, ratio),
            $"{ratio:P0} of the {reference:F0}-line median across the {settledLinesPastFloor.Count} settled patch(es) older than {patchLabel}.",
            reference,
            settledLinesPastFloor.Count);
    }

    /// <summary>
    /// The verdict for one patch, first match wins. The order is the point:
    /// "nothing has aggregated this yet" and "this is aggregated and genuinely short" are
    /// the two causes of the same low number, and they call for opposite reactions — so
    /// they are never collapsed into one "low coverage" state.
    ///
    /// <para>
    /// <paramref name="aggregateRows"/> is deliberately a different question from
    /// <paramref name="lines"/>. Whether the fold has <em>run</em> is answered by any scope
    /// row at all, including the lane-less sentinel rows the ranked directory drops;
    /// whether the patch is <em>rankable</em> is answered by the lane-bearing lines only.
    /// Deciding "not aggregated" from the line count would label a patch whose scope rows
    /// happen to carry no lane as unaggregated, which is the one thing it is not.
    /// </para>
    ///
    /// <para>
    /// Severity depends on whether the patch is the one being served. A thin patch nobody
    /// reads is history; a thin patch behind today's tier list is the site publishing a
    /// ranking it cannot support, which is red.
    /// </para>
    /// </summary>
    public static PatchCoverageVerdict ReadVerdict(
        long matches,
        long aggregateRows,
        long lines,
        long linesPastFloor,
        double bar,
        bool isServed)
        => (matches, aggregateRows) switch
        {
            (<= 0, <= 0) => new PatchCoverageVerdict("unknown", DetectorStatus.Unknown, Judged: false),
            (_, <= 0) => new PatchCoverageVerdict("notAggregated", DetectorStatus.Amber, Judged: false),
            // Aggregated but with nothing the directory can rank. Thin, not unaggregated:
            // the fold ran, and what it produced still cannot carry a patch-scoped page.
            _ when lines <= 0 => new PatchCoverageVerdict("thin", isServed ? DetectorStatus.Red : DetectorStatus.Amber, Judged: true),
            _ when linesPastFloor >= bar => new PatchCoverageVerdict("servable", DetectorStatus.Green, Judged: true),
            _ => new PatchCoverageVerdict("thin", isServed ? DetectorStatus.Red : DetectorStatus.Amber, Judged: true)
        };

    /// <summary>
    /// Middle value, averaging the two middles on an even count. Null on an empty set —
    /// "there is nothing to compare against" is an answer, and 0 would be a bar every
    /// patch clears, including an empty one.
    /// </summary>
    public static double? Median(IReadOnlyList<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2d;
    }
}

/// <summary>The servable bar and the sentence that says where it came from.</summary>
/// <param name="Value">Lines past the floor a patch must reach.</param>
/// <param name="Note">Provenance in words — a bar with no provenance is not an answer.</param>
/// <param name="Reference">The median it was derived from, or null when the fallback applied.</param>
/// <param name="ReferencePatches">How many settled patches fed that median.</param>
internal sealed record PatchCoverageBar(double Value, string Note, double? Reference, int ReferencePatches);

/// <summary>One patch's verdict, its badge colour, and whether it was judged against the bar at all.</summary>
internal sealed record PatchCoverageVerdict(string Verdict, DetectorStatus Status, bool Judged);
