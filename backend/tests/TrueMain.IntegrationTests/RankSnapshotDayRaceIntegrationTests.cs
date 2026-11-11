using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Ingestor.Ranking;
using Microsoft.EntityFrameworkCore;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// #2587: Discovery, LadderSync and AccountRefresh all write rank snapshots, each deciding
/// insert / update against a latest snapshot it preloaded earlier. When another writer stored
/// today's row in between, the insert hit <c>IX_rank_snapshots_account_day</c> and failed the
/// whole save slice. The day already holds a row, so the newest reading has to land in it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RankSnapshotDayRaceIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Morning = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync() => await fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Save_KeepsTheNewestReading_WhenAnotherWriterFilledTheDayAfterThePreload()
    {
        var account = await SeedAccountAsync();
        var writer = new RankSnapshotWriter();
        var sessions = fixture.CreateSessionFactory();

        await using var slow = await sessions.CreateAsync(CancellationToken.None);
        var preloaded = await slow.RankSnapshots.GetLatestForAccountsAsync([account.Id], CancellationToken.None);
        preloaded.Should().BeEmpty();

        await using (var fast = await sessions.CreateAsync(CancellationToken.None))
        {
            var fastAccount = await fast.RiotAccounts.GetByKeysAsync([new(account.PlatformId, account.Puuid)], CancellationToken.None);
            writer.Ingest(fast, fastAccount.Values.Single(), Reading(40), latest: null, Morning)
                .Should().Be(RankSnapshotOutcome.Inserted);
            await fast.SaveChangesAsync(CancellationToken.None);
        }

        var slowAccount = await slow.RiotAccounts.GetByKeysAsync([new(account.PlatformId, account.Puuid)], CancellationToken.None);
        writer.Ingest(slow, slowAccount.Values.Single(), Reading(55), latest: null, Morning.AddMinutes(5))
            .Should().Be(RankSnapshotOutcome.Inserted);
        var save = () => slow.SaveChangesAsync(CancellationToken.None);

        await save.Should().NotThrowAsync();
        await using var verify = fixture.CreateDbContext();
        var row = await verify.RankSnapshots.SingleAsync(s => s.RiotAccountId == account.Id);
        row.LeaguePoints.Should().Be(55);
        row.CapturedAtUtc.Should().Be(Morning.AddMinutes(5));
    }

    [Fact]
    public async Task Save_KeepsTheStoredRow_WhenTheStagedReadingIsOlderThanIt()
    {
        var account = await SeedAccountAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.RankSnapshots.Add(Snapshot(account.Id, Morning.AddHours(2), 70));
            await seed.SaveChangesAsync();
        }

        await using var session = await fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        session.RankSnapshots.Add(Snapshot(account.Id, Morning, 40));
        await session.SaveChangesAsync(CancellationToken.None);

        await using var verify = fixture.CreateDbContext();
        var row = await verify.RankSnapshots.SingleAsync(s => s.RiotAccountId == account.Id);
        row.LeaguePoints.Should().Be(70);
    }

    [Fact]
    public async Task Save_WritesOneRowPerDay_WhenTheSameSessionStagesTheDayTwice()
    {
        var account = await SeedAccountAsync();

        await using var session = await fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        session.RankSnapshots.Add(Snapshot(account.Id, Morning.AddHours(1), 60));
        session.RankSnapshots.Add(Snapshot(account.Id, Morning, 40));
        session.RankSnapshots.Add(Snapshot(account.Id, Morning.AddDays(-1), 20));
        await session.SaveChangesAsync(CancellationToken.None);

        await using var verify = fixture.CreateDbContext();
        var rows = await verify.RankSnapshots
            .Where(s => s.RiotAccountId == account.Id)
            .OrderBy(s => s.CapturedAtUtc)
            .Select(s => s.LeaguePoints)
            .ToListAsync();
        rows.Should().Equal(20, 60);
    }

    [Fact]
    public async Task ClearTracking_DropsTheStagedSnapshots()
    {
        var account = await SeedAccountAsync();

        await using var session = await fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        session.RankSnapshots.Add(Snapshot(account.Id, Morning, 40));
        session.ClearTracking();
        await session.SaveChangesAsync(CancellationToken.None);

        await using var verify = fixture.CreateDbContext();
        (await verify.RankSnapshots.AnyAsync(s => s.RiotAccountId == account.Id)).Should().BeFalse();
    }

    private async Task<RiotAccount> SeedAccountAsync()
    {
        var account = new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = "puuid-raced",
            PlatformId = "KR",
            GameName = "player",
            CreatedAtUtc = Morning.AddDays(-2),
            UpdatedAtUtc = Morning.AddDays(-2)
        };
        await using var db = fixture.CreateDbContext();
        db.RiotAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static RankSnapshotInput Reading(int leaguePoints)
        => new(RankTier.Master, RankDivision.I, leaguePoints, Wins: 10, Losses: 5);

    private static RankSnapshot Snapshot(Guid accountId, DateTime capturedAtUtc, int leaguePoints) => new()
    {
        Id = Guid.NewGuid(),
        RiotAccountId = accountId,
        CapturedAtUtc = capturedAtUtc,
        Tier = RankTier.Master,
        Division = RankDivision.I,
        LeaguePoints = leaguePoints,
        Wins = 10,
        Losses = 5
    };
}
