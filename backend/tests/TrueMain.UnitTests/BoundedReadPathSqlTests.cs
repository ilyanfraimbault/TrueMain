using AwesomeAssertions;
using Data;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the SQL of the read paths bounded by #1233. These are shape assertions, not
/// behaviour: each fix only pays off if the narrowing actually reaches Postgres, and
/// the failure mode when it does not — a client-evaluated group-by, a window applied
/// after the whole table has been read — is invisible in a behavioural test, which
/// still returns the right answer while reading orders of magnitude too many rows.
///
/// <para>No database is involved: <c>ToQueryString()</c> runs the whole EF translation
/// pipeline against the Npgsql provider without opening a connection.</para>
/// </summary>
public sealed class BoundedReadPathSqlTests
{
    private static TrueMainDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-sql-shape;Username=none;Password=none")
            .Options;

        return new TrueMainDbContext(options);
    }

    [Fact]
    public void Retention_observed_patches_are_grouped_by_postgres_not_by_the_client()
    {
        using var db = CreateContext();

        var sql = MatchDataRetentionProcess.ObservedPatchesQuery(db, 420).ToQueryString();

        // The whole point of the shape: a few hundred (platform, version) rows come back
        // instead of one row per retained match. A client-evaluated fallback would emit a
        // bare SELECT over matches with neither GROUP BY nor max().
        sql.Should().Contain("GROUP BY");
        sql.Should().Contain("\"PlatformId\"");
        sql.Should().Contain("\"GameVersion\"");
        sql.Should().Contain("max(");
    }

    [Fact]
    public void Retention_observed_patches_do_not_project_one_row_per_match()
    {
        using var db = CreateContext();

        var sql = MatchDataRetentionProcess.ObservedPatchesQuery(db, 420).ToQueryString();

        // The previous shape ordered every match by start time and projected it. Sorting
        // the whole table is exactly the cost the grouping removes.
        sql.Should().NotContain("ORDER BY");
    }

    [Fact]
    public void Rank_snapshot_lookup_pushes_the_window_into_sql()
    {
        using var db = CreateContext();
        var accountIds = new List<Guid> { Guid.NewGuid() };
        var gameStart = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        var sql = MatchDetailQueryService
            .RankSnapshotsQuery(db, accountIds, gameStart.AddDays(-14), gameStart.AddDays(14))
            .ToQueryString();

        // Both bounds must be real predicates: the account filter alone reads the
        // account's entire rank history, which grows by one row per day forever.
        sql.Should().Contain("\"CapturedAtUtc\" >=");
        sql.Should().Contain("\"CapturedAtUtc\" <=");
    }

    [Fact]
    public void Rank_snapshot_fallback_drops_the_window_predicates()
    {
        using var db = CreateContext();
        var accountIds = new List<Guid> { Guid.NewGuid() };

        var sql = MatchDetailQueryService
            .RankSnapshotsQuery(db, accountIds, null, null)
            .ToQueryString();

        // The fallback for an account with nothing near the game: without it, a rarely
        // refreshed account loses its rank badge instead of costing one extra query.
        sql.Should().NotContain("\"CapturedAtUtc\" >=");
        sql.Should().NotContain("\"CapturedAtUtc\" <=");
        sql.Should().Contain("\"RiotAccountId\"");
    }

    [Fact]
    public void Perk_selection_lookup_filters_on_the_participant_slot_too()
    {
        using var db = CreateContext();

        var sql = ParticipantBuildFactsLoader
            .PerkSelectionsQuery(db, ["EUW1_1", "EUW1_2"], [3])
            .ToQueryString();

        // Still a rectangle — a slot is only meaningful inside its own match — but the
        // match filter alone read the perks of all ten participants of every match in
        // the slice to keep the one that was selected. Asserted on the WHERE clause:
        // the participant column is in the projection either way.
        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        where.Should().Contain("\"ParticipantId\"");
        where.Should().Contain("\"MatchId\"");
    }
}
