using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Ops.Health;

/// <summary>
/// The raw-corpus totals, as the planner's live-tuple estimates of <c>matches</c> and
/// <c>match_participants</c> (#2132) — the same <c>pg_stat_user_tables.n_live_tup</c> the
/// <c>/database</c> panel prints as each table's rows, so the pages that show them
/// (<c>/health</c>, the Overview since #2301) cannot disagree.
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
        var rows = await db.Database
            .SqlQuery<TableRowEstimate>($"""
                SELECT relname AS "TableName", n_live_tup::bigint AS "RowEstimate"
                FROM pg_catalog.pg_stat_user_tables
                WHERE schemaname = 'public' AND relname IN ('matches', 'match_participants')
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
