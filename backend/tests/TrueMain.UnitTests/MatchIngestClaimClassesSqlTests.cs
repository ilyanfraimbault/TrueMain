using AwesomeAssertions;
using Data;
using Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the SQL of the claim's two classes and of the promotion ranking (#1535): breadth only
/// claims accounts never ingested, and a candidate whose account was ingested neither competes
/// for promotion nor keeps a queue place. Shape assertions, run through the Npgsql translation
/// with <c>ToQueryString()</c> and no connection, as in <see cref="BoundedReadPathSqlTests"/> —
/// a predicate that silently stops reaching Postgres would still pass a behavioural test on a
/// small fixture.
/// </summary>
public sealed class MatchIngestClaimClassesSqlTests
{
    private static TrueMainDbContext CreateContext()
        => new(new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-sql-shape;Username=none;Password=none")
            .Options);

    [Fact]
    public void NewCandidate_OnlyTakesAccountsNeverIngested()
    {
        using var db = CreateContext();

        var sql = db.RiotAccounts.Where(MatchIngestClaimClasses.NewCandidate(db)).ToQueryString();

        sql.Should().Contain("\"LastMatchIngestAtUtc\" IS NULL", "an ingested account is depth's, never breadth's");
        sql.Should().Contain("EXISTS", "membership is still a Queued candidate on the account");
        sql.Should().Contain("main_candidates");
    }

    [Fact]
    public void EstablishedMain_DoesNotFilterOnIngestHistory()
    {
        using var db = CreateContext();

        var sql = db.RiotAccounts.Where(MatchIngestClaimClasses.EstablishedMain(db)).ToQueryString();
        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];

        // Depth is where an ingested account comes back when due; gating it on history would
        // stop refreshing exactly the accounts breadth now hands over.
        where.Should().NotContain("\"LastMatchIngestAtUtc\"");
        sql.Should().Contain("main_champion_stats");
    }

    [Fact]
    public void PromotionRanking_ExcludesCandidatesOfIngestedAccounts()
    {
        using var db = CreateContext();

        var sql = MatchIngestClaimClasses.PromotionRanking(db, "EUW1", 10, [1, 2]).ToQueryString();

        sql.Should().Contain("NOT EXISTS (");
        sql.Should().Contain("riot_accounts");
        sql.Should().Contain("\"LastMatchIngestAtUtc\" IS NOT NULL");
        sql.Should().Contain("ORDER BY", "the ranking itself is unchanged");
        sql.Should().Contain("LIMIT");
    }

    [Fact]
    public void QueuedForIngestedAccounts_SelectsOnlyQueuedRowsOfIngestedAccountsInABoundedBatch()
    {
        using var db = CreateContext();

        var sql = MatchIngestClaimClasses.QueuedForIngestedAccounts(db, "EUW1", 500).ToQueryString();

        sql.Should().Contain("EXISTS (");
        sql.Should().NotContain("NOT EXISTS (");
        sql.Should().Contain("\"LastMatchIngestAtUtc\" IS NOT NULL");
        sql.Should().Contain("LIMIT");
    }
}
