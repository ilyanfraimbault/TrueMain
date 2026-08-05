using Data;
using Data.Metrics.Mongo;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// The live "what is on disk right now" panel: every Postgres table and every Mongo
/// collection, measured on request rather than read from the daily snapshots (#1023).
/// Both engines share one volume, so a Postgres-only list understated the disk.
/// </summary>
public sealed class TableStatsQueryService(TrueMainDbContext db, IMongoStorageStatsReader mongoStats)
    : ITableStatsQueryService
{
    public async Task<IReadOnlyList<TableStatRow>> GetAsync(CancellationToken ct)
    {
        // Physical sizes straight from pg_catalog for the public schema. relid is
        // the table OID, so the size functions need no quoting/escaping of names.
        // RowEstimate is the planner's live-tuple estimate (can be 0 before the
        // first ANALYZE), not an exact COUNT — exact counts would mean a full
        // scan per table, which this ops panel does not warrant.
        FormattableString sql = $"""
            SELECT
                relname AS "TableName",
                n_live_tup::bigint AS "RowEstimate",
                pg_total_relation_size(relid)::bigint AS "TotalBytes",
                pg_relation_size(relid)::bigint AS "TableBytes",
                pg_indexes_size(relid)::bigint AS "IndexBytes"
            FROM pg_catalog.pg_stat_user_tables
            WHERE schemaname = 'public'
            ORDER BY pg_total_relation_size(relid) DESC, relname
            """;

        var rows = await db.Database.SqlQuery<TableStatRowResult>(sql).ToListAsync(ct);

        var stats = rows
            .Select(row => new TableStatRow
            {
                Engine = StorageEngines.Postgres,
                TableName = row.TableName,
                RowEstimate = row.RowEstimate,
                TotalBytes = row.TotalBytes,
                TableBytes = row.TableBytes,
                IndexBytes = row.IndexBytes
            })
            .ToList();

        // Null when Mongo is unconfigured: the list is then Postgres-only, which the
        // panel reports as an engine it did not measure rather than as an empty one.
        var mongo = await mongoStats.GetAsync(ct);
        if (mongo is not null)
        {
            stats.AddRange(mongo.Collections.Select(collection => new TableStatRow
            {
                Engine = StorageEngines.Mongo,
                TableName = collection.TableName,
                RowEstimate = collection.RowEstimate,
                TotalBytes = collection.TotalBytes,
                TableBytes = collection.TableBytes,
                IndexBytes = collection.IndexBytes
            }));
        }

        // One ordering across both engines — the question the panel answers is "what
        // is biggest on this disk", and that does not stop at an engine boundary.
        return stats
            .OrderByDescending(row => row.TotalBytes)
            .ThenBy(row => row.TableName, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record TableStatRowResult(
        string TableName,
        long RowEstimate,
        long TotalBytes,
        long TableBytes,
        long IndexBytes);
}
