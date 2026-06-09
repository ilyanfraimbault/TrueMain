using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class TableStatsQueryService(TrueMainDbContext db) : ITableStatsQueryService
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

        return rows
            .Select(row => new TableStatRow
            {
                TableName = row.TableName,
                RowEstimate = row.RowEstimate,
                TotalBytes = row.TotalBytes,
                TableBytes = row.TableBytes,
                IndexBytes = row.IndexBytes
            })
            .ToList();
    }

    private sealed record TableStatRowResult(
        string TableName,
        long RowEstimate,
        long TotalBytes,
        long TableBytes,
        long IndexBytes);
}
