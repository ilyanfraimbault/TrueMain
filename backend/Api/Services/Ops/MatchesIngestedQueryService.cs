using System.Globalization;
using System.Text.Json;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Buckets the recorded <c>MatchIngestion</c> run summaries into a throughput series
/// (#1025). See <see cref="MatchesIngestedReadModel"/> for why the source is the run
/// summaries rather than <c>matches.CreatedAtUtc</c>.
///
/// <para>
/// The counters are summed in memory because they live inside
/// <c>ProcessRunDocument.SummaryJson</c>, which is stored as opaque JSON <em>text</em>
/// so the admin receives byte-identical bytes to what the recorder wrote. Mongo cannot
/// sum a field inside a string, so the split is: the server does the indexed range
/// scan and projects two fields, this does the parsing and the arithmetic.
/// </para>
/// </summary>
public sealed class MatchesIngestedQueryService(
    IProcessRunStore store,
    IOptions<MongoLoggingOptions> mongoOptions,
    TimeProvider timeProvider) : IMatchesIngestedQueryService
{
    /// <summary>The process whose summaries carry the ingestion counters.</summary>
    private const string ProcessName = "MatchIngestion";

    private const int DefaultWindowDays = 30;

    /// <summary>
    /// Upper bound on the requested window. Beyond the run-retention TTL there is
    /// nothing to find anyway, and an unbounded value would only widen the scan.
    /// </summary>
    private const int MaxWindowDays = 365;

    public async Task<MatchesIngestedReadModel> GetAsync(
        IngestionTimeGranularity granularity,
        int? windowDays,
        CancellationToken ct)
    {
        var days = Math.Clamp(windowDays is > 0 ? windowDays.Value : DefaultWindowDays, 1, MaxWindowDays);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var since = nowUtc.Date.AddDays(-days);

        var retentionDays = (int)Math.Round(mongoOptions.Value.ProcessRunsRetention.TotalDays);

        var runs = await store.GetRunSummariesAsync(ProcessName, since, ct);

        var model = new MatchesIngestedReadModel
        {
            WindowDays = days,
            RetentionDays = retentionDays,
        };

        if (runs.Count == 0)
        {
            return model;
        }

        var totals = new Dictionary<DateTime, Counters>();
        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();

            var bucket = Truncate(run.StartedAtUtc, granularity);
            var counters = totals.TryGetValue(bucket, out var existing) ? existing : new Counters();

            // A run always counts, summary or not: a failed or still-running pass is
            // an attempt the period genuinely contains, and dropping it would make a
            // crash-looping ingestor look like an idle one.
            counters.Runs++;

            if (TryReadCounters(run.SummaryJson, out var inserted, out var skipped, out var timelines))
            {
                counters.Inserted += inserted;
                counters.Skipped += skipped;
                counters.Timelines += timelines;
            }

            totals[bucket] = counters;
        }

        // Zero-fill from the oldest run we actually have, never from the window's
        // edge: a period older than that was not measured (retention took it), and
        // filling it with zeros would assert an idle pipeline we have no record of.
        var earliest = runs[0].StartedAtUtc;
        var buckets = new List<MatchesIngestedBucket>();
        var cursor = Truncate(earliest, granularity);
        var last = Truncate(nowUtc, granularity);

        while (cursor <= last)
        {
            var counters = totals.TryGetValue(cursor, out var found) ? found : new Counters();
            buckets.Add(new MatchesIngestedBucket(
                FormatBucket(cursor),
                counters.Inserted,
                counters.Skipped,
                counters.Timelines,
                counters.Runs));

            cursor = Advance(cursor, granularity);
        }

        return model with
        {
            Buckets = buckets,
            EarliestRunAtUtc = earliest,
        };
    }

    /// <summary>
    /// Reads the three counters off the stored summary. Returns false when the run
    /// recorded none (a failure, an abandoned run, or a no-work pass whose summary has
    /// a different shape) — the run still counts, its counters are simply absent
    /// rather than zero-by-assumption.
    /// </summary>
    private static bool TryReadCounters(string? summaryJson, out long inserted, out long skipped, out long timelines)
    {
        inserted = 0;
        skipped = 0;
        timelines = 0;

        if (string.IsNullOrWhiteSpace(summaryJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(summaryJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            inserted = ReadInt64(document.RootElement, "matchesInserted");
            skipped = ReadInt64(document.RootElement, "matchesSkipped");
            timelines = ReadInt64(document.RootElement, "timelinesUpdated");
            return true;
        }
        catch (JsonException)
        {
            // Same posture as ProcessRunSummaryParsing: a malformed summary is a
            // missing summary, not a reason to fail the whole panel.
            return false;
        }
    }

    private static long ReadInt64(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt64(out var number)
            ? number
            : 0;

    /// <summary>
    /// Period start in UTC. Weeks are Monday-based to match Postgres'
    /// <c>date_trunc('week')</c>, which the sibling matches-over-time series uses —
    /// two charts side by side must not disagree about where a week begins.
    /// </summary>
    private static DateTime Truncate(DateTime instant, IngestionTimeGranularity granularity)
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

    private static DateTime Advance(DateTime bucket, IngestionTimeGranularity granularity)
        => granularity switch
        {
            IngestionTimeGranularity.Day => bucket.AddDays(1),
            IngestionTimeGranularity.Week => bucket.AddDays(7),
            IngestionTimeGranularity.Month => bucket.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
        };

    private static string FormatBucket(DateTime bucket)
        => bucket.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private struct Counters
    {
        public long Inserted;
        public long Skipped;
        public long Timelines;
        public int Runs;
    }
}
