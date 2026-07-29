namespace TrueMain.Services.Ops;

/// <summary>
/// Least-squares linear projection of database size against time (#925): given the
/// daily snapshots, when does the volume cross a given fill level?
///
/// <para>
/// Deliberately the simplest model that answers the operational question. Growth here
/// is dominated by a roughly constant ingestion rate against a fixed retention window,
/// so a straight line is the honest shape; anything fancier would imply a confidence
/// the data does not support. The failure modes are handled explicitly instead of
/// being smoothed over — see <see cref="Project"/>.
/// </para>
/// </summary>
internal static class StorageForecastCalculator
{
    /// <summary>
    /// Fewer points than this and no projection is offered at all. Two points always
    /// fit a line perfectly, so a two-day fit would report a crossing date with total
    /// confidence off what may be a single retention pass or one backfill.
    /// </summary>
    public const int MinimumPoints = 3;

    /// <summary>
    /// Fits <c>bytes = slope · day + intercept</c> over <paramref name="points"/> and
    /// returns the fit plus the date each threshold is crossed.
    ///
    /// <para>Returns <see langword="null"/> — meaning "no forecast", never a guess — when:
    /// there are fewer than <see cref="MinimumPoints"/> days; every point falls on the
    /// same day (a zero-variance x, which would divide by zero); or the fitted slope is
    /// not positive, i.e. storage is flat or shrinking and will not cross anything.
    /// A threshold already below the current fitted size yields a crossing date in the
    /// past, which is the correct answer and is left for the caller to present.</para>
    /// </summary>
    public static StorageForecast? Project(
        IReadOnlyList<StorageForecastPoint> points,
        IReadOnlyList<long> thresholdsBytes)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(thresholdsBytes);

        if (points.Count < MinimumPoints)
        {
            return null;
        }

        // Days since the first sample as x, so the intercept is the fitted size at the
        // window's start and the slope is directly "bytes per day" — the number the
        // panel shows — rather than a coefficient against an epoch.
        var origin = points[0].DateUtc;
        var xs = points.Select(point => (point.DateUtc - origin).TotalDays).ToList();
        var ys = points.Select(point => (double)point.DatabaseBytes).ToList();

        var meanX = xs.Average();
        var meanY = ys.Average();

        var varianceX = xs.Sum(x => (x - meanX) * (x - meanX));
        if (varianceX <= 0)
        {
            return null;
        }

        var covariance = xs.Zip(ys, (x, y) => (x - meanX) * (y - meanY)).Sum();
        var slope = covariance / varianceX;
        var intercept = meanY - (slope * meanX);

        if (slope <= 0)
        {
            return null;
        }

        var crossings = thresholdsBytes
            .Select(threshold => new StorageThresholdCrossing(
                threshold,
                // Clamped to a century in EITHER direction: a near-flat slope pushes
                // the crossing astronomically far out, and — for a threshold already
                // below the fitted line — astronomically far back. Both overflow
                // DateTime, and both are noise rather than information.
                ProjectCrossing(origin, slope, intercept, threshold)))
            .ToList();

        return new StorageForecast(
            (long)Math.Round(slope),
            crossings);
    }

    /// <summary>
    /// Days from <paramref name="origin"/> until the fitted line reaches
    /// <paramref name="threshold"/>, as a date.
    /// </summary>
    /// <remarks>
    /// The magnitude clamp is symmetric on purpose. Clamping only the positive side
    /// left the mirror case live: a threshold well below the fitted intercept — i.e.
    /// a fill level already breached, the very situation this panel exists to surface
    /// — combined with a small-but-positive slope yields a hugely negative
    /// <c>days</c>, and <see cref="DateTime.AddDays"/> then threw
    /// <see cref="ArgumentOutOfRangeException"/>. Nothing upstream caught it, so it
    /// took down the whole <c>GET /ops/db/history</c> response, charts included, not
    /// just the forecast card.
    /// </remarks>
    private static DateTime? ProjectCrossing(DateTime origin, double slope, double intercept, long threshold)
    {
        var days = (threshold - intercept) / slope;
        return double.IsNaN(days) || double.IsInfinity(days) || Math.Abs(days) > 36_500
            ? null
            : origin.AddDays(days);
    }
}

/// <summary>One observed day of total database size, the forecast's input.</summary>
internal sealed record StorageForecastPoint(DateTime DateUtc, long DatabaseBytes);

/// <summary>The fitted growth rate and the projected crossing of each threshold.</summary>
internal sealed record StorageForecast(
    long BytesPerDay,
    IReadOnlyList<StorageThresholdCrossing> Crossings);

/// <summary>
/// When the fitted line reaches <paramref name="ThresholdBytes"/>.
/// <paramref name="ProjectedAtUtc"/> is null when the crossing lands more than a
/// century away in either direction — no meaningful date at this rate, rather than a
/// spurious one. A date in the past means the threshold has already been passed.
/// </summary>
internal sealed record StorageThresholdCrossing(long ThresholdBytes, DateTime? ProjectedAtUtc);
