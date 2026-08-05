using System.Globalization;

namespace TrueMain.Services.Ops;

/// <summary>
/// Allowed x-axis granularities for a series read from the process-run summaries.
/// Narrower than <see cref="MatchTimeGranularity"/> on purpose:
/// <c>Patch</c> is a property of the games, not of when the pipeline ran, and
/// <c>Year</c> cannot fill more than one bucket under the 180-day run retention.
/// </summary>
public enum IngestionTimeGranularity
{
    Day,
    Week,
    Month
}

/// <summary>
/// Period arithmetic shared by every series built from <c>process_runs</c>
/// (matches ingested #1025, candidate funnel #1024). One implementation because two
/// throughput charts read side by side must not disagree about where a week begins —
/// a series whose Monday is another series' Sunday reads as a one-day pipeline lag
/// that never happened.
/// </summary>
internal static class RunTimeBuckets
{
    /// <summary>
    /// Period start in UTC. Weeks are Monday-based to match Postgres'
    /// <c>date_trunc('week')</c>, which the matches-over-time series uses.
    /// </summary>
    public static DateTime Truncate(DateTime instant, IngestionTimeGranularity granularity)
    {
        var day = DateTime.SpecifyKind(instant.Date, DateTimeKind.Utc);

        return granularity switch
        {
            IngestionTimeGranularity.Day => day,
            IngestionTimeGranularity.Week => day.AddDays(-((int)day.DayOfWeek + 6) % 7),
            IngestionTimeGranularity.Month => new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
        };
    }

    public static DateTime Advance(DateTime bucket, IngestionTimeGranularity granularity)
        => granularity switch
        {
            IngestionTimeGranularity.Day => bucket.AddDays(1),
            IngestionTimeGranularity.Week => bucket.AddDays(7),
            IngestionTimeGranularity.Month => bucket.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
        };

    public static string Format(DateTime bucket)
        => bucket.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
