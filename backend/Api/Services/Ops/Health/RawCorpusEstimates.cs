using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Ops.Health;

/// <summary>
/// The raw-corpus totals of <c>matches</c> and <c>match_participants</c>, as the planner
/// estimates them (#2132): <c>pg_class.reltuples</c> scaled to the table's current size,
/// the figure the Overview's 7/30-day windows are estimated from too (#2301).
///
/// <para>
/// Not <c>pg_stat_user_tables.n_live_tup</c>, which this read used at first: the cumulative
/// statistics are discarded on crash recovery and only rebuilt by the next autovacuum, so
/// after an unclean restart they reported a fraction of a percent of the table's rows — and
/// the windows capped at that total read the same. <c>reltuples</c> lives in the catalog and survives.
/// </para>
///
/// <para>
/// An exact count is a full scan of the participant table: on a cold cache it ran into the
/// API's 60 s <c>statement_timeout</c>. The estimate is a catalog lookup. It is table-wide
/// rather than filtered on the queue, which costs nothing in practice: storage keeps the
/// ranked queue only (#680) and retention drains anything else on every pass.
/// </para>
/// </summary>
internal sealed record RawCorpusEstimates(long? Matches, long? Participants, string? UnknownReason)
{
    public static async Task<RawCorpusEstimates> ReadAsync(TrueMainDbContext db, CancellationToken ct)
    {
        // The planner's own arithmetic (estimate_rel_size): the tuple density recorded by the
        // last VACUUM/ANALYZE, scaled to the table's current size. A table never vacuumed nor
        // analyzed has no density yet (reltuples = -1) and falls back to the live-tuple counter.
        var rows = await db.Database
            .SqlQuery<TableRowEstimate>($"""
                SELECT c.relname AS "TableName",
                       (CASE
                            WHEN c.reltuples < 0 OR c.relpages = 0 THEN COALESCE(s.n_live_tup, 0)
                            ELSE c.reltuples / c.relpages
                                 * (pg_catalog.pg_relation_size(c.oid) / current_setting('block_size')::int)
                        END)::bigint AS "RowEstimate"
                FROM pg_catalog.pg_class c
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_catalog.pg_stat_user_tables s ON s.relid = c.oid
                WHERE n.nspname = 'public' AND c.relkind = 'r'
                  AND c.relname IN ('matches', 'match_participants')
                """)
            .ToListAsync(ct);

        return From(rows);
    }

    /// <summary>
    /// Picks the two tables out of the catalog rows. A table the statistics view does
    /// not list has no estimate to give, which is reported as such rather than as zero.
    /// </summary>
    public static RawCorpusEstimates From(IReadOnlyCollection<TableRowEstimate> rows)
    {
        long? Estimate(string table) => rows
            .Where(row => row.TableName == table)
            .Select(row => (long?)row.RowEstimate)
            .FirstOrDefault();

        var matches = Estimate("matches");
        var participants = Estimate("match_participants");

        return matches is null || participants is null
            ? new RawCorpusEstimates(matches, participants, "The table statistics do not list the raw match tables.")
            : new RawCorpusEstimates(matches, participants, null);
    }
}

internal sealed record TableRowEstimate(string TableName, long RowEstimate);
