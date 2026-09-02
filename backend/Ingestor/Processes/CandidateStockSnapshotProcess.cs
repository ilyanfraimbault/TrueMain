using Data;
using Data.Entities;
using Data.Metrics.Mongo;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes;

/// <summary>
/// Records how many candidates sit in each status, per platform, into the
/// <c>candidate_stock_snapshots</c> Mongo collection (#1403), so the admin candidates
/// panel can chart the funnel's <em>level</em> over time next to the throughput series
/// that already chart its flow (#1024).
///
/// <para>
/// <b>Why a snapshot and not a query over history.</b> The stock cannot be
/// reconstructed from <c>main_candidates</c> after the fact: there is no
/// <c>QueuedAtUtc</c>, so Scored and Queued cannot be told apart in the past, and
/// retention's pruning and the demotion drain delete rows outright, so every past
/// level would be understated by whatever has since been removed. The same reasoning
/// that keeps the funnel off row counts (#1024) is what forces this to be measured
/// forward-only.
/// </para>
///
/// <para>
/// <b>Cheap enough to run every pass.</b> The whole step is one grouped index-only
/// scan of <c>main_candidates</c> — measured at ~190 ms over 745k rows in prod — and
/// one small bulk upsert of well under a hundred documents. Like
/// <see cref="StorageSnapshotProcess"/>, the store keys documents on the period rather
/// than on the run, so the pipeline running back-to-back refreshes the hour's reading
/// instead of appending a point per run, and no "have I already run this hour" guard
/// is needed.
/// </para>
///
/// <para>
/// It runs after <see cref="MatchDataRetentionProcess"/> for the same reason the
/// storage snapshot does: retention prunes stale candidates and demotes the excess of
/// an over-full queue back to <c>Scored</c>, so the reading taken before it is a peak
/// the pipeline does not actually sit at.
/// </para>
/// </summary>
public sealed class CandidateStockSnapshotProcess(
    ILogger<CandidateStockSnapshotProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    ICandidateStockSnapshotStore store,
    TimeProvider timeProvider) : IIngestorProcess
{
    public string Name => "CandidateStockSnapshot";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var groups = await db.MainCandidates
            .AsNoTracking()
            .GroupBy(candidate => new { candidate.PlatformId, candidate.Status })
            .Select(group => new
            {
                group.Key.PlatformId,
                group.Key.Status,
                Count = (long)group.LongCount()
            })
            .ToListAsync(ct);

        var samples = BuildSamples(groups.Select(row => (row.PlatformId, row.Status, row.Count)));

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var written = await store.UpsertHourAsync(nowUtc, samples, ct);

        var total = samples.Sum(sample => sample.Count);

        logger.LogInformation(
            "Candidate stock snapshot summary: platforms={Platforms}, series={Series}, written={Written}, candidates={Candidates}.",
            samples.Select(sample => sample.PlatformId).Distinct(StringComparer.Ordinal).Count(),
            samples.Count,
            written,
            total);

        return new CandidateStockSnapshotSummary(
            samples.Select(sample => sample.PlatformId).Distinct(StringComparer.Ordinal).Count(),
            samples.Count,
            written,
            total);
    }

    /// <summary>
    /// Expands the grouped counts into one sample per (observed platform, <em>every</em>
    /// status). A status with no rows produces no group, and leaving it out would make
    /// the read side unable to tell "measured, and it was empty" from "this hour was
    /// never measured" — which is exactly the distinction the panel has to draw, since
    /// <c>New</c> and <c>Processing</c> read 0 whenever the pipeline is healthy. The
    /// platform list is the observed one: a platform holding no candidates at all has
    /// nothing to say about the funnel, and inventing a row for every platform the
    /// configuration mentions would assert a measurement of a population that does not
    /// exist.
    /// </summary>
    internal static List<CandidateStockSample> BuildSamples(
        IEnumerable<(string PlatformId, MainCandidateStatus Status, long Count)> groups)
    {
        var counts = new Dictionary<(string PlatformId, MainCandidateStatus Status), long>();
        foreach (var group in groups)
        {
            counts[(group.PlatformId, group.Status)] = group.Count;
        }

        var platforms = counts.Keys
            .Select(key => key.PlatformId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(platform => platform, StringComparer.Ordinal);

        var samples = new List<CandidateStockSample>();
        foreach (var platform in platforms)
        {
            foreach (var status in Enum.GetValues<MainCandidateStatus>())
            {
                samples.Add(new CandidateStockSample(
                    platform,
                    status.ToString(),
                    counts.GetValueOrDefault((platform, status))));
            }
        }

        return samples;
    }
}
