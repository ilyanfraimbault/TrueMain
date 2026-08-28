using System.Text.RegularExpressions;
using AwesomeAssertions;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the repo's actual relational naming convention so a new mapping cannot quietly
/// widen the schema's one inconsistency.
/// </summary>
/// <remarks>
/// The convention is <b>tables snake_case, columns PascalCase</b> — not "snake_case
/// everywhere", which is what a reader assumes from the table names alone. Every column in
/// the schema is quoted PascalCase (<c>"ChampionId"</c>, <c>"IsMain"</c>,
/// <c>"PowerspikeAggregated"</c> — visible in the raw SQL filters and in
/// <c>Data/DataQuality/ChampionDimensionCanonicalKeys.cs</c>) with exactly one historical
/// exception, <c>elo_bracket</c>, mapped by hand in seven configurations. Renaming either
/// side is a heavy migration over frozen tables for no gain, so the schema stands and these
/// tests describe it. What they prevent is the mix getting <em>worse</em>: a new snake_case
/// column dropped into a PascalCase table, or the <c>elo_bracket</c> spelling spreading
/// because it was mistaken for the rule.
/// </remarks>
public sealed class SchemaNamingConventionTests
{
    /// <summary>
    /// The historical exception, allow-listed by name. Do not extend this list: an entry
    /// here is the inconsistency spreading, which is what the test exists to stop.
    /// </summary>
    private static readonly HashSet<string> SnakeCaseColumnExceptions =
        new(StringComparer.Ordinal) { "elo_bracket" };

    private static readonly Regex PascalCase =
        new("^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SnakeCase =
        new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Every_table_is_snake_case()
    {
        using TrueMainDbContext context = CreateContext();

        List<string> offenders = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Select(tableName => tableName!)
            .Distinct(StringComparer.Ordinal)
            .Where(tableName => !SnakeCase.IsMatch(tableName))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "tables are snake_case; map the new one rather than starting a second convention");
    }

    [Fact]
    public void Every_column_is_pascal_case_apart_from_the_documented_exception()
    {
        using TrueMainDbContext context = CreateContext();

        List<string> offenders = [];

        foreach (IEntityType entityType in context.Model.GetEntityTypes())
        {
            string? tableName = entityType.GetTableName();
            if (tableName is null)
            {
                continue;
            }

            foreach (IProperty property in entityType.GetProperties())
            {
                string columnName = property.GetColumnName();

                // Shadow concurrency tokens map onto Postgres system columns (xmin on
                // riot_accounts, #231): no physical column of ours, nothing to name.
                if (columnName.Length == 0 || columnName == "xmin")
                {
                    continue;
                }

                if (PascalCase.IsMatch(columnName) || SnakeCaseColumnExceptions.Contains(columnName))
                {
                    continue;
                }

                offenders.Add($"{tableName}.{columnName}");
            }
        }

        offenders.Sort(StringComparer.Ordinal);

        offenders.Should().BeEmpty(
            "columns are PascalCase; elo_bracket is the one historical exception and must not spread");
    }

    // The model is built from the configurations, not read from a database, so no
    // connection is ever opened — UseNpgsql only selects the provider whose conventions
    // decide the mapping.
    private static TrueMainDbContext CreateContext()
    {
        DbContextOptions<TrueMainDbContext> options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;

        return new TrueMainDbContext(options);
    }
}
