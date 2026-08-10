namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Physical storage footprint of a single table in the <c>public</c> schema,
/// sourced from <c>pg_catalog</c>. <see cref="RowEstimate"/> is the planner's
/// live-tuple estimate (<c>pg_stat_user_tables.n_live_tup</c>), not an exact count,
/// and can read 0 until the table is analysed/vacuumed. Byte figures are
/// <c>pg_total_relation_size</c> (total) = <c>pg_relation_size</c> (table heap) +
/// <c>pg_indexes_size</c> (indexes) + TOAST.
/// </summary>
public sealed record TableStatRow
{
    /// <summary>
    /// Which engine the object belongs to: <c>postgres</c> or <c>mongo</c> (#1023). A
    /// table and a collection are not the same kind of object, and two of them share a
    /// name (<c>process_runs</c>, <c>seed_requests</c>), so the list says which is
    /// which rather than merging them into one undifferentiated column.
    /// </summary>
    public string Engine { get; init; } = string.Empty;

    /// <summary>The Postgres table name, or the Mongo collection name.</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Planner live-tuple estimate for Postgres (0 before the first ANALYZE); an exact
    /// document count for Mongo, which reports one cheaply.
    /// </summary>
    public long RowEstimate { get; init; }

    public long TotalBytes { get; init; }

    public long TableBytes { get; init; }

    public long IndexBytes { get; init; }
}
