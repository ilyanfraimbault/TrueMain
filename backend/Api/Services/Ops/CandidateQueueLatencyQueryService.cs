using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// The queue-latency snapshot behind the candidate funnel (#1024). See
/// <see cref="CandidateQueueLatencyReadModel"/> for why this is a snapshot over retained
/// rows and not a series — the funnel's throughput chart is the historical half, and it
/// deliberately reads a different source.
/// </summary>
public sealed class CandidateQueueLatencyQueryService(TrueMainDbContext db, TimeProvider timeProvider)
    : ICandidateQueueLatencyQueryService
{
    public async Task<CandidateQueueLatencyReadModel> GetAsync(CancellationToken ct)
    {
        // One pass over main_candidates: the percentiles need every duration ordered, so
        // they are computed server-side with percentile_cont rather than by hauling a
        // timestamp pair per row into memory. FILTER keeps each leg to the rows that
        // carry both of its ends, so a candidate scored but not yet validated counts in
        // the first leg only, instead of being dropped from both.
        //
        // No parameters and no index: this scans the table, the same way the overview's
        // status breakdown does, and it is an admin-only read taken on demand.
        FormattableString sql =
            $"""
             SELECT
                 COUNT(*)::bigint AS "RetainedCandidates",
                 COUNT(*) FILTER (WHERE "ScoredAtUtc" IS NOT NULL)::bigint AS "ScoredSamples",
                 percentile_cont(0.5) WITHIN GROUP (
                     ORDER BY EXTRACT(EPOCH FROM ("ScoredAtUtc" - "DiscoveredAtUtc"))::double precision)
                     FILTER (WHERE "ScoredAtUtc" IS NOT NULL) AS "ScoredMedianSeconds",
                 percentile_cont(0.9) WITHIN GROUP (
                     ORDER BY EXTRACT(EPOCH FROM ("ScoredAtUtc" - "DiscoveredAtUtc"))::double precision)
                     FILTER (WHERE "ScoredAtUtc" IS NOT NULL) AS "ScoredP90Seconds",
                 COUNT(*) FILTER (WHERE "ScoredAtUtc" IS NOT NULL AND "ValidatedAtUtc" IS NOT NULL)::bigint
                     AS "ValidatedSamples",
                 percentile_cont(0.5) WITHIN GROUP (
                     ORDER BY EXTRACT(EPOCH FROM ("ValidatedAtUtc" - "ScoredAtUtc"))::double precision)
                     FILTER (WHERE "ScoredAtUtc" IS NOT NULL AND "ValidatedAtUtc" IS NOT NULL)
                     AS "ValidatedMedianSeconds",
                 percentile_cont(0.9) WITHIN GROUP (
                     ORDER BY EXTRACT(EPOCH FROM ("ValidatedAtUtc" - "ScoredAtUtc"))::double precision)
                     FILTER (WHERE "ScoredAtUtc" IS NOT NULL AND "ValidatedAtUtc" IS NOT NULL)
                     AS "ValidatedP90Seconds"
             FROM main_candidates
             """;

        var row = await db.Database.SqlQuery<LatencyRow>(sql).SingleAsync(ct);

        return new CandidateQueueLatencyReadModel
        {
            RetainedCandidates = row.RetainedCandidates,
            DiscoveredToScored = new CandidateLatencyLeg(
                row.ScoredSamples,
                row.ScoredMedianSeconds,
                row.ScoredP90Seconds),
            ScoredToValidated = new CandidateLatencyLeg(
                row.ValidatedSamples,
                row.ValidatedMedianSeconds,
                row.ValidatedP90Seconds),
            AsOfUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
    }

    // percentile_cont returns NULL over an empty filtered set, which is why every
    // percentile is nullable here while the counts are not.
    private sealed record LatencyRow(
        long RetainedCandidates,
        long ScoredSamples,
        double? ScoredMedianSeconds,
        double? ScoredP90Seconds,
        long ValidatedSamples,
        double? ValidatedMedianSeconds,
        double? ValidatedP90Seconds);
}
