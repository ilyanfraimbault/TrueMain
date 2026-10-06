using System.Text;
using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingestor.Processes.Components.IncrementalFolds;

/// <summary>
/// One additive aggregate table, as the incremental folds write it (#1239): a batch of
/// accumulated rows goes in as one <c>INSERT … SELECT FROM unnest(…)</c>, and a row whose
/// key already exists has each counter <em>added</em> to the stored one
/// (<c>ON CONFLICT (keys) DO UPDATE SET x = table.x + EXCLUDED.x</c>) rather than replaced.
/// That is what lets a fold touch each match exactly once and still converge on the totals
/// a full recompute would give.
///
/// <para>
/// Every table it writes carries a generated <c>"Id"</c> (<c>gen_random_uuid()</c>) and an
/// <c>"AggregatedAtUtc"</c> stamp overwritten on conflict, and the key columns must be
/// exactly the columns of the table's unique index (the conflict target is matched as a
/// set, so their order does not matter). Column names are compile-time constants of the
/// declaring fold, never input, and every value goes through a typed array parameter.
/// </para>
/// </summary>
internal sealed class AdditiveUpsert<TRow>(string table)
{
    private readonly List<Column> _columns = [];
    private string? _sql;

    /// <summary>A column of the conflict target, inserted as-is and never updated.</summary>
    public AdditiveUpsert<TRow> Key<TValue>(string column, string sqlType, Func<TRow, TValue> select)
        => Add(column, sqlType, additive: false, rows => rows.Select(select).ToArray());

    /// <summary>A counter: inserted as-is, added to the stored value on conflict.</summary>
    public AdditiveUpsert<TRow> Sum<TValue>(string column, string sqlType, Func<TRow, TValue> select)
        => Add(column, sqlType, additive: true, rows => rows.Select(select).ToArray());

    /// <summary>The statement this table runs, built once from the declared columns.</summary>
    public string Sql => _sql ??= BuildSql();

    /// <summary>Upserts <paramref name="rows"/>; a no-op (no round trip) when there are none.</summary>
    public async Task ExecuteAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<TRow> rows,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var materialized = rows.ToList();
        var parameters = new object[_columns.Count + 1];
        parameters[0] = new NpgsqlParameter("aggAt", aggregatedAtUtc);
        for (var i = 0; i < _columns.Count; i++)
        {
            parameters[i + 1] = new NpgsqlParameter(ParameterName(i), _columns[i].Values(materialized));
        }

        await db.Database.ExecuteSqlRawAsync(Sql, parameters, ct);
    }

    private AdditiveUpsert<TRow> Add(
        string column,
        string sqlType,
        bool additive,
        Func<IReadOnlyList<TRow>, Array> values)
    {
        if (_sql is not null)
        {
            throw new InvalidOperationException($"The {table} upsert is already built; declare every column first.");
        }

        _columns.Add(new Column(column, sqlType, additive, values));
        return this;
    }

    private string BuildSql()
    {
        if (!_columns.Exists(c => !c.Additive) || !_columns.Exists(c => c.Additive))
        {
            throw new InvalidOperationException(
                $"The {table} upsert needs at least one key column and one additive column.");
        }

        var aliases = Enumerable.Range(0, _columns.Count).Select(ParameterName).ToList();

        var sql = new StringBuilder();
        sql.Append("INSERT INTO ").Append(table).AppendLine();
        sql.Append("    (\"Id\", ")
            .AppendJoin(", ", _columns.Select(c => Quote(c.Name)))
            .AppendLine(", \"AggregatedAtUtc\")");
        sql.Append("SELECT gen_random_uuid(), ")
            .AppendJoin(", ", aliases.Select(a => "t." + a))
            .AppendLine(", @aggAt");
        sql.Append("FROM unnest(")
            .AppendJoin(", ", _columns.Select((c, i) => $"@{aliases[i]}::{c.SqlType}[]"))
            .AppendLine(")");
        sql.Append("    AS t(").AppendJoin(", ", aliases).AppendLine(")");
        sql.Append("ON CONFLICT (")
            .AppendJoin(", ", _columns.Where(c => !c.Additive).Select(c => Quote(c.Name)))
            .AppendLine(") DO UPDATE SET");

        foreach (var column in _columns.Where(c => c.Additive))
        {
            var name = Quote(column.Name);
            sql.Append("    ").Append(name).Append(" = ").Append(table).Append('.').Append(name)
                .Append(" + EXCLUDED.").Append(name).AppendLine(",");
        }

        sql.Append("    \"AggregatedAtUtc\" = EXCLUDED.\"AggregatedAtUtc\"");
        return sql.ToString();
    }

    private static string ParameterName(int index) => $"c{index}";

    private static string Quote(string column) => $"\"{column}\"";

    private sealed record Column(string Name, string SqlType, bool Additive, Func<IReadOnlyList<TRow>, Array> Values);
}
