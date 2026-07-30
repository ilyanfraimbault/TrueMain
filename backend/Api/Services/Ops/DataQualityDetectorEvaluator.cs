using System.Globalization;

namespace TrueMain.Services.Ops;

/// <summary>
/// The judgement half of the data-quality detectors (#924): everything that turns a
/// measured number into a green/amber/red verdict, with no database and no clock of its
/// own. Kept pure and separate for the same reason
/// <see cref="StorageForecastCalculator"/> is — the thresholds are the part that will be
/// argued about, so they have to be testable without a Postgres container.
/// </summary>
internal static class DataQualityDetectorEvaluator
{
    /// <summary>
    /// Classifies a "more is worse" measurement — a count, a ratio, an age in hours —
    /// against its amber and red levels.
    ///
    /// <para>
    /// Reaching a level is <c>&gt;=</c>, not <c>&gt;</c>: a threshold of 1 duplicate group
    /// has to fire on the first one, and an off-by-one here is the difference between a
    /// panel that reports #911 and one that does not. A level of <c>0</c> or less is
    /// <b>disabled</b> (it can never be reached), which is how a warning-only signal is
    /// configured. A <see langword="null"/> measurement is
    /// <see cref="DetectorStatus.Unknown"/> — never green: "I could not measure this" and
    /// "I measured this and it is fine" are different answers, and conflating them is how
    /// a dashboard lies.
    /// </para>
    /// </summary>
    public static DetectorStatus Classify(double? value, double amberAt, double redAt)
    {
        if (value is null || double.IsNaN(value.Value))
        {
            return DetectorStatus.Unknown;
        }

        if (redAt > 0 && value.Value >= redAt)
        {
            return DetectorStatus.Red;
        }

        return amberAt > 0 && value.Value >= amberAt ? DetectorStatus.Amber : DetectorStatus.Green;
    }

    /// <inheritdoc cref="Classify(double?, double, double)"/>
    public static DetectorStatus Classify(long? value, long amberAt, long redAt)
        => Classify(value is null ? null : (double)value.Value, amberAt, redAt);

    /// <summary>
    /// Rolls several row verdicts into the card's own, with precedence
    /// <c>Red &gt; Amber &gt; Unknown &gt; Green</c>.
    ///
    /// <para>
    /// Unknown sits <em>between</em> amber and green on purpose. Putting it above red
    /// would let one unmeasurable platform hide a real failure on another; putting it
    /// below green would let a card claim to be clean while part of it was never
    /// measured. So a card with one unknown row and nine green ones reads unknown, and a
    /// card with one unknown row and one red one reads red.
    /// </para>
    /// </summary>
    public static DetectorStatus Worst(IEnumerable<DetectorStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var sawUnknown = false;
        var sawAmber = false;
        var sawAny = false;

        foreach (var status in statuses)
        {
            sawAny = true;
            switch (status)
            {
                case DetectorStatus.Red:
                    return DetectorStatus.Red;
                case DetectorStatus.Amber:
                    sawAmber = true;
                    break;
                case DetectorStatus.Unknown:
                    sawUnknown = true;
                    break;
                case DetectorStatus.Green:
                default:
                    break;
            }
        }

        if (sawAmber)
        {
            return DetectorStatus.Amber;
        }

        // No rows at all is not a pass either: the detector produced nothing to judge.
        return sawUnknown || !sawAny ? DetectorStatus.Unknown : DetectorStatus.Green;
    }

    /// <summary>
    /// Share of <paramref name="part"/> in <paramref name="total"/> as a percentage, or
    /// <see langword="null"/> when there is nothing to divide — an empty sample has no
    /// ratio, and returning 0 would read as "perfectly clean".
    /// </summary>
    public static double? Percent(long part, long total)
        => total <= 0 ? null : part * 100d / total;

    /// <summary>
    /// Whole hours-and-fraction between <paramref name="sinceUtc"/> and
    /// <paramref name="nowUtc"/>, or <see langword="null"/> when the timestamp is absent.
    /// Never negative: a clock skew that puts the reading in the future is reported as
    /// zero age rather than as a negative one, which would classify as green through the
    /// threshold comparison anyway but read as nonsense on the panel.
    /// </summary>
    public static double? AgeHours(DateTime? sinceUtc, DateTime nowUtc)
        => sinceUtc is null ? null : Math.Max(0, (nowUtc - sinceUtc.Value).TotalHours);

    /// <summary>
    /// The orphan-share reading over a two-window sample: the level in the newer half and
    /// how far it moved from the older half.
    ///
    /// <para>
    /// Both halves must be non-empty for a trend to exist. With one half empty the level
    /// is still reported (from whichever half has rows) but the rise is
    /// <see langword="null"/> — a "trend" computed against nothing would show either a
    /// full-scale jump or a full-scale drop depending on which side was missing.
    /// </para>
    /// </summary>
    public static OrphanRatioReading ReadOrphanRatio(
        long recentOrphans,
        long recentParticipants,
        long previousOrphans,
        long previousParticipants)
    {
        var recentPercent = Percent(recentOrphans, recentParticipants);
        var previousPercent = Percent(previousOrphans, previousParticipants);

        // Level: prefer the newer half — that is the state of ingestion right now — and
        // fall back to the whole sample when it is empty (a platform whose newest matches
        // all predate the split still has an orphan share worth reporting).
        var level = recentPercent ?? Percent(
            recentOrphans + previousOrphans,
            recentParticipants + previousParticipants);

        var risePoints = recentPercent is not null && previousPercent is not null
            ? recentPercent.Value - previousPercent.Value
            : (double?)null;

        return new OrphanRatioReading(level, previousPercent, risePoints);
    }

    /// <summary>
    /// Flags patches whose match count is abnormally thin against the median of the
    /// comparable ones.
    ///
    /// <para>
    /// <b>The first and last patch in the corpus are never judged.</b> The newest one is
    /// still filling — hours into a patch it legitimately holds a handful of games — and
    /// the oldest is being trimmed by retention, so it legitimately holds a fraction of
    /// what it once did. Comparing either against the median flags a healthy pipeline
    /// every single day, which is the fastest way to get a detector ignored. They are
    /// still returned, marked unjudged, so the operator sees the whole distribution.
    /// </para>
    /// </summary>
    /// <param name="patchesOldestFirst">
    /// One entry per patch, ordered oldest to newest. Ordering is the caller's
    /// responsibility because only it knows how to compare patch strings.
    /// </param>
    /// <param name="thinRatio">Fraction of the median below which a patch is flagged.</param>
    /// <param name="minComparablePatches">
    /// Comparable (judged) patches required before any verdict is given at all.
    /// </param>
    public static PatchVolumeReading ReadPatchVolumes(
        IReadOnlyList<PatchVolume> patchesOldestFirst,
        double thinRatio,
        int minComparablePatches)
    {
        ArgumentNullException.ThrowIfNull(patchesOldestFirst);

        // Indices of the patches eligible for judgement: everything but the two edges.
        // With 2 or fewer patches that set is empty, which is the unknown case below.
        var judgedIndices = Enumerable
            .Range(0, patchesOldestFirst.Count)
            .Where(index => index > 0 && index < patchesOldestFirst.Count - 1)
            .ToList();

        if (judgedIndices.Count < Math.Max(1, minComparablePatches))
        {
            return new PatchVolumeReading(
                null,
                [.. patchesOldestFirst.Select(patch => new PatchVolumeVerdict(patch, false, false))],
                judgedIndices.Count);
        }

        var median = Median([.. judgedIndices.Select(index => (double)patchesOldestFirst[index].Matches)]);
        var floor = median * thinRatio;

        var verdicts = new List<PatchVolumeVerdict>(patchesOldestFirst.Count);
        for (var index = 0; index < patchesOldestFirst.Count; index++)
        {
            var judged = judgedIndices.Contains(index);
            verdicts.Add(new PatchVolumeVerdict(
                patchesOldestFirst[index],
                judged,
                judged && patchesOldestFirst[index].Matches < floor));
        }

        return new PatchVolumeReading(median, verdicts, judgedIndices.Count);
    }

    /// <summary>Lower-case wire name for a status, matching the API's camelCase policy.</summary>
    public static string ToWireName(this DetectorStatus status)
        => status switch
        {
            DetectorStatus.Green => "green",
            DetectorStatus.Amber => "amber",
            DetectorStatus.Red => "red",
            _ => "unknown"
        };

    /// <summary>Formats an age in hours the way every detector row says it.</summary>
    public static string? FormatAge(double? hours)
    {
        if (hours is null)
        {
            return null;
        }

        return hours.Value switch
        {
            < 1 => string.Create(CultureInfo.InvariantCulture, $"{hours.Value * 60:F0} min ago"),
            < 48 => string.Create(CultureInfo.InvariantCulture, $"{hours.Value:F1} h ago"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{hours.Value / 24:F1} d ago")
        };
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;

        // Even count: the mean of the two middle values. Odd: the middle one. Spelled out
        // rather than reused from a helper because the integer division above is exactly
        // where a median goes subtly wrong.
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

/// <summary>A detector verdict. Ordered by declaration only; precedence lives in
/// <see cref="DataQualityDetectorEvaluator.Worst"/>.</summary>
internal enum DetectorStatus
{
    /// <summary>The measurement could not be taken. Never a pass.</summary>
    Unknown,

    /// <summary>Measured and within its configured band.</summary>
    Green,

    /// <summary>Measured and past its amber level.</summary>
    Amber,

    /// <summary>Measured and past its red level.</summary>
    Red
}

/// <summary>
/// The orphan-share reading: the current level, the previous window's level, and the
/// movement between them, all in percent / percentage points, any of them null when the
/// underlying window was empty.
/// </summary>
internal sealed record OrphanRatioReading(double? Percent, double? PreviousPercent, double? RisePoints);

/// <summary>One patch's ingested match count.</summary>
internal sealed record PatchVolume(string Patch, long Matches);

/// <summary>A patch's volume verdict: whether it was judged at all, and whether it is thin.</summary>
internal sealed record PatchVolumeVerdict(PatchVolume Patch, bool Judged, bool Thin);

/// <summary>
/// The patch-volume reading. <paramref name="MedianMatches"/> is null when there were too
/// few comparable patches to compute one, in which case no verdict is a pass.
/// </summary>
internal sealed record PatchVolumeReading(
    double? MedianMatches,
    IReadOnlyList<PatchVolumeVerdict> Verdicts,
    int ComparablePatches);
