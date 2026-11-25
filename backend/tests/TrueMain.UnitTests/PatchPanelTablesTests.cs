using System.Reflection;
using AwesomeAssertions;
using Data;
using Data.Aggregation;
using Data.Entities;
using Ingestor.Processes;
using Ingestor.Processes.Components.IncrementalFolds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace TrueMain.UnitTests;

/// <summary>
/// The fold registry against the schema it describes, read from the EF model without a
/// database: every per-patch table is classified, and every additive upsert writes columns that
/// exist with the declared type, keyed on a unique index.
/// </summary>
public sealed class PatchPanelTablesTests
{
    /// <summary>
    /// <c>Patch</c>-keyed tables that are not panels: the light per-patch level kept after a
    /// purge (#1467, #1956), the purge marker, the whole-table game-count rollup and raw match rows.
    /// </summary>
    private static readonly Type[] NotPanels =
    [
        typeof(ChampionPatchStat),
        typeof(ChampionBanStat),
        typeof(BanScopeTotal),
        typeof(ChampionBuildPurgedPatch),
        typeof(ChampionGameCount),
        typeof(Match),
        typeof(TruncatedMatchSeat)
    ];

    [Fact]
    public void EveryPatchKeyedTable_IsAPanelTableOrAnExplicitException()
    {
        using var db = CreateContext();

        var patchKeyed = db.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty(PatchPanelTables.PatchColumn) is not null)
            .Select(entity => entity.ClrType);

        patchKeyed.Should().BeEquivalentTo(PatchPanelTables.All.Select(table => table.Entity).Concat(NotPanels));
    }

    [Fact]
    public void EveryAdditiveUpsert_WritesExistingColumnsOfTheirDeclaredType_OnAUniqueKey()
    {
        using var db = CreateContext();
        var tables = db.Model.GetRelationalModel().Tables.ToDictionary(table => table.Name);

        var upserts = typeof(IIngestorProcess).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(field => field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(AdditiveUpsert<>))
            .Select(field => (Name: $"{field.DeclaringType!.Name}.{field.Name}", Upsert: field.GetValue(null)!))
            .ToList();

        upserts.Should().HaveCountGreaterThanOrEqualTo(8);
        foreach (var (name, upsert) in upserts)
        {
            var tableName = (string)upsert.GetType().GetProperty("Table", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(upsert)!;
            var columns = (IReadOnlyList<(string Name, string SqlType, bool Additive)>)upsert.GetType()
                .GetProperty("Columns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(upsert)!;

            tables.Should().ContainKey(tableName, name);
            var table = tables[tableName];
            foreach (var column in columns)
            {
                var stored = table.FindColumn(column.Name);
                stored.Should().NotBeNull($"{name} writes {tableName}.{column.Name}");
                SqlFamily(stored!.StoreType).Should().Be(SqlFamily(column.SqlType), $"{name} declares {tableName}.{column.Name} as {column.SqlType}");
            }

            var keys = columns.Where(column => !column.Additive).Select(column => column.Name).ToHashSet();
            table.Indexes.Cast<ITableIndex>().Where(index => index.IsUnique).Select(index => index.Columns.Select(c => c.Name).ToHashSet())
                .Concat(table.UniqueConstraints.Select(constraint => constraint.Columns.Select(c => c.Name).ToHashSet()))
                .Should().Contain(unique => unique.SetEquals(keys), $"{name}'s ON CONFLICT needs a unique key on ({string.Join(", ", keys)})");
        }
    }

    /// <summary>
    /// The type an array parameter is cast to only has to convert implicitly into the column:
    /// a <c>text[]</c> fills a <c>character varying(16)</c> column, the length is the column's.
    /// </summary>
    private static string SqlFamily(string storeType)
    {
        var bare = storeType.Split('(')[0].Trim().ToLowerInvariant();
        return bare is "character varying" or "varchar" ? "text" : bare;
    }

    private static TrueMainDbContext CreateContext()
        => new(new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options);
}
