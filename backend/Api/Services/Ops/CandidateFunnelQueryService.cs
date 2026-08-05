using System.Text.Json;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Buckets the recorded run summaries of the six processes that move a candidate into
/// one funnel series (#1024). See <see cref="CandidateFunnelReadModel"/> for why the
/// source is the run summaries rather than <c>main_candidates</c> row counts.
///
/// <para>
/// One store call covers all six: the summaries live in <c>ProcessRunDocument.SummaryJson</c>
/// as opaque JSON <em>text</em>, so Mongo cannot sum a counter inside them anyway, and the
/// split is the same as the ingestion series' — the server does the indexed
/// <c>(processName, startedAtUtc)</c> scan, this does the parsing and the arithmetic.
/// </para>
/// </summary>
public sealed class CandidateFunnelQueryService(
    IProcessRunStore store,
    IOptions<MongoLoggingOptions> mongoOptions,
    TimeProvider timeProvider) : ICandidateFunnelQueryService
{
    private const string Discovery = "Discovery";
    private const string Harvest = "Harvest";
    private const string ManualSeed = "ManualSeed";
    private const string Scoring = "Scoring";
    private const string MatchIngestion = "MatchIngestion";
    private const string MainAnalysis = "MainAnalysis";

    /// <summary>
    /// Every process that moves a candidate between statuses. <c>MatchDataRetention</c>
    /// is deliberately absent: it deletes stale candidates rather than advancing them, so
    /// its <c>prunedCandidates</c> belongs to the retention panel, not to a funnel that
    /// reads left-to-right.
    /// </summary>
    private static readonly string[] ContributingProcesses =
        [Discovery, Harvest, ManualSeed, Scoring, MatchIngestion, MainAnalysis];

    /// <summary>
    /// The forward-only counter (#1024). Its <em>presence</em> is the signal: a
    /// MatchIngestion run recorded before the deploy has no such key, and reporting that
    /// as a validated count of zero would claim a stall the pipeline never had.
    /// </summary>
    private const string ValidatedCounter = "accountsValidated";

    private const int DefaultWindowDays = 30;

    /// <summary>
    /// Upper bound on the requested window. Beyond the run-retention TTL there is
    /// nothing to find anyway, and an unbounded value would only widen the scan.
    /// </summary>
    private const int MaxWindowDays = 365;

    public async Task<CandidateFunnelReadModel> GetAsync(
        IngestionTimeGranularity granularity,
        int? windowDays,
        CancellationToken ct)
    {
        var days = Math.Clamp(windowDays is > 0 ? windowDays.Value : DefaultWindowDays, 1, MaxWindowDays);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var since = nowUtc.Date.AddDays(-days);

        var retentionDays = (int)Math.Round(mongoOptions.Value.ProcessRunsRetention.TotalDays);

        var runs = await store.GetRunSummariesAsync(ContributingProcesses, since, ct);

        var model = new CandidateFunnelReadModel
        {
            WindowDays = days,
            RetentionDays = retentionDays,
        };

        if (runs.Count == 0)
        {
            return model;
        }

        var totals = new Dictionary<DateTime, Counters>();
        DateTime? validatedFirstMeasuredAt = null;

        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();

            var bucket = RunTimeBuckets.Truncate(run.StartedAtUtc, granularity);
            var counters = totals.TryGetValue(bucket, out var existing) ? existing : new Counters();

            // A run always counts, summary or not: a failed or still-running pass is an
            // attempt the period genuinely contains, and dropping it would make a
            // crash-looping pipeline look like an idle one.
            counters.Runs++;

            using (var document = ProcessRunSummaryParsing.TryParseObject(run.SummaryJson))
            {
                if (document is not null)
                {
                    Accumulate(ref counters, run.ProcessName, document.RootElement, run.StartedAtUtc, ref validatedFirstMeasuredAt);
                }
            }

            totals[bucket] = counters;
        }

        // Zero-fill from the oldest run we actually have, never from the window's edge: a
        // period older than that was not measured (retention took it), and filling it
        // with zeros would assert an idle pipeline we have no record of.
        var earliest = runs[0].StartedAtUtc;
        var validatedFrom = validatedFirstMeasuredAt is { } measuredAt
            ? RunTimeBuckets.Truncate(measuredAt, granularity)
            : (DateTime?)null;

        var buckets = new List<CandidateFunnelBucket>();
        var cursor = RunTimeBuckets.Truncate(earliest, granularity);
        var last = RunTimeBuckets.Truncate(nowUtc, granularity);

        while (cursor <= last)
        {
            var counters = totals.TryGetValue(cursor, out var found) ? found : new Counters();

            // Zero once the counter existed, absent before: past that boundary a quiet
            // period really did validate nothing, and saying so is the point of the panel.
            var validated = validatedFrom is { } from && cursor >= from ? counters.Validated : (long?)null;

            buckets.Add(new CandidateFunnelBucket(
                RunTimeBuckets.Format(cursor),
                counters.IntakeLadder,
                counters.IntakeHarvest,
                counters.IntakeManual,
                counters.Scored,
                counters.Promoted,
                validated,
                counters.Demoted,
                counters.Runs));

            cursor = RunTimeBuckets.Advance(cursor, granularity);
        }

        return model with
        {
            Buckets = buckets,
            EarliestRunAtUtc = earliest,
            ValidatedFirstMeasuredAtUtc = validatedFirstMeasuredAt,
        };
    }

    /// <summary>
    /// Folds one run's summary into its period. A summary of an unexpected shape — a
    /// no-work or skipped pass, or a failure that recorded a different record — simply
    /// contributes nothing: every read below is key-based and absent keys read as 0.
    /// </summary>
    private static void Accumulate(
        ref Counters counters,
        string processName,
        JsonElement summary,
        DateTime startedAtUtc,
        ref DateTime? validatedFirstMeasuredAt)
    {
        switch (processName)
        {
            case Discovery:
                counters.IntakeLadder += SumOverPlatforms(summary, "candidatesInserted");
                break;

            case Harvest:
                counters.IntakeHarvest += ProcessRunSummaryParsing.ReadInt64(summary, "candidatesInserted");
                break;

            case ManualSeed:
                counters.IntakeManual += ProcessRunSummaryParsing.ReadInt64(summary, "candidatesQueued");
                break;

            case Scoring:
                counters.Scored += SumOverPlatforms(summary, "scored");
                counters.Promoted += SumOverPlatforms(summary, "queued");
                break;

            case MatchIngestion:
                if (summary.TryGetProperty(ValidatedCounter, out var validated)
                    && validated.ValueKind == JsonValueKind.Number
                    && validated.TryGetInt64(out var validatedCount))
                {
                    counters.Validated += validatedCount;

                    // Runs arrive oldest-first, so the first run carrying the key is the
                    // earliest period the series may report a validated count for.
                    validatedFirstMeasuredAt ??= startedAtUtc;
                }

                break;

            case MainAnalysis:
                counters.Demoted += ProcessRunSummaryParsing.ReadInt64(summary, "demotedAccounts");
                break;
        }
    }

    /// <summary>
    /// Sums a counter across the summary's per-platform breakdown. Discovery and Scoring
    /// report per platform and have no run-level total, so the platform array <em>is</em>
    /// the number — an absent or non-array <c>platforms</c> (a no-work pass) sums to 0.
    /// </summary>
    private static long SumOverPlatforms(JsonElement summary, string property)
    {
        if (!summary.TryGetProperty("platforms", out var platforms)
            || platforms.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var total = 0L;
        foreach (var platform in platforms.EnumerateArray())
        {
            if (platform.ValueKind == JsonValueKind.Object)
            {
                total += ProcessRunSummaryParsing.ReadInt64(platform, property);
            }
        }

        return total;
    }

    private struct Counters
    {
        public long IntakeLadder;
        public long IntakeHarvest;
        public long IntakeManual;
        public long Scored;
        public long Promoted;
        public long Validated;
        public long Demoted;
        public int Runs;
    }
}
