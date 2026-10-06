using AwesomeAssertions;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions.Builds;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins that <see cref="ChampionCohort"/>'s composable queries reach Postgres whole: every
/// clause of the cohort in the SQL, nothing left to the client. A clause the provider
/// could not translate would not fail a behavioural test with a friendly corpus; it would
/// either throw at request time or, worse, silently widen the population again.
///
/// <para>No database is involved: <c>ToQueryString()</c> runs the EF translation pipeline
/// against the Npgsql provider without opening a connection.</para>
/// </summary>
public sealed class ChampionCohortSqlTests
{
    private static TrueMainDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-sql-shape;Username=none;Password=none")
            .Options;

        return new TrueMainDbContext(options);
    }

    [Fact]
    public void Members_carries_every_clause_of_the_cohort()
    {
        using var db = CreateContext();

        var sql = ChampionCohort.Members(db, 420, "16.4")
            .Where(p => p.ChampionId == 1 && p.TeamPosition == "MIDDLE")
            .ToQueryString();

        sql.Should().Contain("\"RiotAccountId\" IS NOT NULL", "it is the partial index's filter");
        sql.Should().Contain("\"IsMain\"", "the cohort is the champion's mains");
        sql.Should().Contain("\"GameDurationSeconds\" >= 300", "a remake is not a game");
        sql.Should().Contain("\"QueueId\"");
        sql.Should().Contain("\"Patch\"");
        sql.Should().Contain("main_champion_stats");
        sql.Should().NotContain("\"IsActive\"", "IsActive retires a main from ingestion, not from history");
    }

    [Fact]
    public void Games_is_the_match_half_only()
    {
        using var db = CreateContext();

        var sql = ChampionCohort.Games(db, 420, null).ToQueryString();

        sql.Should().Contain("\"GameDurationSeconds\" >= 300");
        sql.Should().Contain("\"QueueId\"");
        sql.Should().NotContain("main_champion_stats");
    }

    [Fact]
    public void Item_timings_compose_the_raw_unnest_under_the_cohort_in_one_statement()
    {
        using var db = CreateContext();

        var sql = ChampionItemTimingsQueryService
            .TimingsQuery(db, 1, "MIDDLE", 420, "16.4", ["GOLD"], 5)
            .ToQueryString();

        sql.Should().Contain("jsonb_array_elements", "the purchase unnest stays in SQL");
        sql.Should().Contain("\"IsMain\"", "the cohort is composed into the same statement");
        sql.Should().Contain("\"GameDurationSeconds\" >= 300");
        sql.Should().Contain("GROUP BY");
        sql.Should().Contain("HAVING", "the sample floor is applied before rows cross the wire");
        sql.Should().Contain("ORDER BY");
    }
}
