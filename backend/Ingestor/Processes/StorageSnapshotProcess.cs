using Data;
using Data.Metrics.Mongo;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes;

/// <summary>
/// Records the day's Postgres storage footprint — one row per user table plus the
/// measured database size — into the <c>db_table_size_snapshots</c> Mongo collection
/// (#925), so the admin database panel can chart growth and forecast when the volume
/// fills up. Disk pressure is the production constraint that caused #680, and until
/// now the panel only ever showed "right now" with nothing to extrapolate from.
///
/// <para>
/// <b>Cheap enough to run every pass.</b> The whole step is one <c>pg_catalog</c>
/// query over ~60 rows and one small bulk upsert, so it is not worth a scheduler:
/// prod runs the pipeline with <c>RunOnce</c> + <c>restart: unless-stopped</c>, i.e.
/// back-to-back many times a day, and the store keys documents on the day rather than
/// the wall clock. Repeated runs therefore refresh the day's reading instead of
/// appending points, and the series stays exactly one point per table per day — the
/// latest one of that day. No "have I already run today" guard is needed, which also
/// means a container restart can never lose or duplicate a day.
/// </para>
///
/// <para>
/// It runs last in the pipeline, after <see cref="MatchDataRetentionProcess"/>, so the
/// figure recorded is the steady-state size after the run's deletions rather than the
/// peak before them — a forecast fitted on pre-retention peaks would predict a disk
/// exhaustion that retention is already preventing.
/// </para>
/// </summary>
public sealed class StorageSnapshotProcess(
    ILogger<StorageSnapshotProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IDbStorageSnapshotStore store,
    TimeProvider timeProvider) : IIngestorProcess
{
    public string Name => "StorageSnapshot";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Same source as the live admin panel's TableStatsQueryService, deliberately:
        // the history must be measured the same way as the "now" column it sits next
        // to, or the newest history point would disagree with the live reading.
        // RowEstimate is the planner's live-tuple estimate, not an exact count — exact
        // counts would mean a full scan per table for an ops chart.
        var samples = await db.Database
            .SqlQuery<TableSizeRow>($"""
                SELECT
                    relname AS "TableName",
                    n_live_tup::bigint AS "RowEstimate",
                    pg_total_relation_size(relid)::bigint AS "TotalBytes",
                    pg_relation_size(relid)::bigint AS "TableBytes",
                    pg_indexes_size(relid)::bigint AS "IndexBytes"
                FROM pg_catalog.pg_stat_user_tables
                WHERE schemaname = 'public'
                """)
            .ToListAsync(ct);

        // Measured, not summed from the tables above: that sum only covers public
        // user tables, while the disk actually holds catalogs and everything else too.
        var databaseBytes = await db.Database
            .SqlQuery<long>($"SELECT pg_database_size(current_database())::bigint AS \"Value\"")
            .SingleAsync(ct);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var written = await store.UpsertDayAsync(
            nowUtc,
            databaseBytes,
            [.. samples.Select(row => new DbTableSizeSample(
                row.TableName,
                row.RowEstimate,
                row.TotalBytes,
                row.TableBytes,
                row.IndexBytes))],
            ct);

        logger.LogInformation(
            "Storage snapshot summary: tables={Tables}, written={Written}, databaseBytes={DatabaseBytes}.",
            samples.Count,
            written,
            databaseBytes);

        return new StorageSnapshotSummary(samples.Count, written, databaseBytes);
    }

    private sealed record TableSizeRow(
        string TableName,
        long RowEstimate,
        long TotalBytes,
        long TableBytes,
        long IndexBytes);
}
