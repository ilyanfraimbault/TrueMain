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
    public string TableName { get; init; } = string.Empty;

    public long RowEstimate { get; init; }

    public long TotalBytes { get; init; }

    public long TableBytes { get; init; }

    public long IndexBytes { get; init; }
}
