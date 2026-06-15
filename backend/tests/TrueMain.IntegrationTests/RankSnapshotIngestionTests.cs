using Core.Lol.Identifiers;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class RankSnapshotIngestionTests
{
    private const string Platform = "KR";

    private readonly PostgresFixture _fixture;

    public RankSnapshotIngestionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunCoreAsync_FirstInsert_PersistsExactSoloEntry()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-first");

        var process = BuildProcess(
            soloEntry: SoloEntry("GOLD", "II", 50, wins: 10, losses: 5));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var snapshots = await verify.RankSnapshots
            .Where(s => s.RiotAccountId == account.Id)
            .ToListAsync();

        snapshots.Should().ContainSingle();
        var snap = snapshots[0];
        snap.Tier.Should().Be("GOLD");
        snap.Division.Should().Be("II");
        snap.LeaguePoints.Should().Be(50);
        snap.Wins.Should().Be(10);
        snap.Losses.Should().Be(5);
        snap.CapturedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RunCoreAsync_UnchangedRank_DoesNotInsertNewRow()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-unchanged");
        await SeedSnapshotAsync(account.Id, "GOLD", "II", 50, DateTime.UtcNow.AddHours(-1));

        var process = BuildProcess(
            soloEntry: SoloEntry("GOLD", "II", 50, wins: 12, losses: 6));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var count = await verify.RankSnapshots.CountAsync(s => s.RiotAccountId == account.Id);
        count.Should().Be(1);
    }

    [Fact]
    public async Task RunCoreAsync_TierChange_InsertsNewSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-tier-change");
        await SeedSnapshotAsync(account.Id, "GOLD", "I", 100, DateTime.UtcNow.AddHours(-2));

        var process = BuildProcess(
            soloEntry: SoloEntry("PLATINUM", "IV", 0));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var snapshots = await verify.RankSnapshots
            .Where(s => s.RiotAccountId == account.Id)
            .OrderByDescending(s => s.CapturedAtUtc)
            .ToListAsync();

        snapshots.Should().HaveCount(2);
        snapshots[0].Tier.Should().Be("PLATINUM");
        snapshots[0].Division.Should().Be("IV");
        snapshots[0].LeaguePoints.Should().Be(0);
    }

    [Fact]
    public async Task RunCoreAsync_DivisionChange_InsertsNewSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-division-change");
        await SeedSnapshotAsync(account.Id, "GOLD", "II", 100, DateTime.UtcNow.AddHours(-2));

        var process = BuildProcess(
            soloEntry: SoloEntry("GOLD", "I", 5));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var snapshots = await verify.RankSnapshots
            .Where(s => s.RiotAccountId == account.Id)
            .OrderByDescending(s => s.CapturedAtUtc)
            .ToListAsync();

        snapshots.Should().HaveCount(2);
        snapshots[0].Tier.Should().Be("GOLD");
        snapshots[0].Division.Should().Be("I");
    }

    [Fact]
    public async Task RunCoreAsync_LpDelta_InsertsNewSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-lp-delta");
        await SeedSnapshotAsync(account.Id, "GOLD", "II", 50, DateTime.UtcNow.AddHours(-2));

        var process = BuildProcess(
            soloEntry: SoloEntry("GOLD", "II", 73));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var snapshots = await verify.RankSnapshots
            .Where(s => s.RiotAccountId == account.Id)
            .OrderByDescending(s => s.CapturedAtUtc)
            .ToListAsync();

        snapshots.Should().HaveCount(2);
        snapshots[0].LeaguePoints.Should().Be(73);
    }

    [Fact]
    public async Task RunCoreAsync_UnrankedAccount_DoesNotInsertSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-unranked");

        // Returns only RANKED_FLEX_SR — should be ignored since we only track soloQ.
        var process = BuildProcess(extraEntries: new[]
        {
            new RiotLeagueEntryByPuuidDto
            {
                QueueType = "RANKED_FLEX_SR",
                Tier = "SILVER",
                Rank = "III",
                LeaguePoints = 42
            }
        });

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var count = await verify.RankSnapshots.CountAsync(s => s.RiotAccountId == account.Id);
        count.Should().Be(0);
    }

    [Fact]
    public async Task RunCoreAsync_EmptyLeagueResponse_DoesNotInsertSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-empty");

        var process = BuildProcess(); // no entries

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var count = await verify.RankSnapshots.CountAsync(s => s.RiotAccountId == account.Id);
        count.Should().Be(0);
    }

    [Fact]
    public async Task RunCoreAsync_ProfileFailure_StillInsertsRankSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-profile-fail");
        var originalName = account.GameName;

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(Arg.Any<string>(), Arg.Any<RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns<Task<RiotAccountDto>>(_ => throw new HttpRequestException("simulated 500"));

        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient.GetLeagueEntriesByPuuidAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RiotLeagueEntryByPuuidDto>
            {
                SoloEntry("GOLD", "IV", 22)
            }));

        var process = BuildProcessWithClients(accountClient, platformClient);
        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var snap = await verify.RankSnapshots.SingleAsync(s => s.RiotAccountId == account.Id);
        snap.Tier.Should().Be("GOLD");

        var stored = await verify.RiotAccounts.SingleAsync(a => a.Id == account.Id);
        stored.GameName.Should().Be(originalName, "profile update was skipped due to the simulated error");
    }

    [Fact]
    public async Task RunCoreAsync_RankFailure_StillUpdatesProfile()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-rank-fail");

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(Arg.Any<string>(), Arg.Any<RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RiotAccountDto
            {
                Puuid = account.Puuid,
                GameName = "renamed-player",
                TagLine = "KR2"
            }));

        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient.GetLeagueEntriesByPuuidAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<List<RiotLeagueEntryByPuuidDto>>>(_ => throw new HttpRequestException("simulated 503"));

        var process = BuildProcessWithClients(accountClient, platformClient);
        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var stored = await verify.RiotAccounts.SingleAsync(a => a.Id == account.Id);
        stored.GameName.Should().Be("renamed-player");
        stored.TagLine.Should().Be("KR2");

        (await verify.RankSnapshots.AnyAsync(s => s.RiotAccountId == account.Id))
            .Should().BeFalse();
    }

    [Fact]
    public async Task RunCoreAsync_SuccessfulRankWrite_BumpsLastRankSyncAtUtc()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-bump");

        var process = BuildProcess(soloEntry: SoloEntry("GOLD", "II", 50));
        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var stored = await verify.RiotAccounts.SingleAsync(a => a.Id == account.Id);
        stored.LastRankSyncAtUtc.Should().NotBeNull();
        stored.LastRankSyncAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RunCoreAsync_RecentLastRankSync_SkipsLeagueByPuuidCall()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-skip-fresh");

        // Simulate a snapshot the Discovery flow just wrote.
        await using (var seed = _fixture.CreateDbContext())
        {
            var tracked = await seed.RiotAccounts.SingleAsync(a => a.Id == account.Id);
            tracked.LastRankSyncAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await seed.SaveChangesAsync();
        }

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(Arg.Any<string>(), Arg.Any<RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RiotAccountDto
            {
                Puuid = account.Puuid,
                GameName = "player",
                TagLine = "KR1"
            }));

        var platformClient = Substitute.For<IRiotPlatformClient>();
        // If the league call is made, return entries that would force a snapshot — the test
        // would then fail by seeing the snapshot appear.
        platformClient.GetLeagueEntriesByPuuidAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RiotLeagueEntryByPuuidDto>
            {
                SoloEntry("CHALLENGER", "I", 800)
            }));

        var process = BuildProcessWithClients(accountClient, platformClient);
        await process.RunCoreAsync(CancellationToken.None);

        await platformClient.DidNotReceive().GetLeagueEntriesByPuuidAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var verify = _fixture.CreateDbContext();
        var snapshots = await verify.RankSnapshots.Where(s => s.RiotAccountId == account.Id).ToListAsync();
        snapshots.Should().BeEmpty("the rank call was skipped because LastRankSyncAtUtc is within the freshness window");
    }

    [Fact]
    public async Task DeletingRiotAccount_CascadesSnapshots()
    {
        await _fixture.ResetDatabaseAsync();
        var account = await SeedAccountAsync("puuid-cascade");
        await SeedSnapshotAsync(account.Id, "GOLD", "II", 50, DateTime.UtcNow.AddHours(-2));
        await SeedSnapshotAsync(account.Id, "GOLD", "II", 73, DateTime.UtcNow.AddHours(-1));

        await using (var deleteDb = _fixture.CreateDbContext())
        {
            await deleteDb.RiotAccounts.Where(a => a.Id == account.Id).ExecuteDeleteAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        (await verify.RankSnapshots.AnyAsync(s => s.RiotAccountId == account.Id))
            .Should().BeFalse();
    }

    private AccountRefreshProcess BuildProcess(
        RiotLeagueEntryByPuuidDto? soloEntry = null,
        IEnumerable<RiotLeagueEntryByPuuidDto>? extraEntries = null)
    {
        var entries = new List<RiotLeagueEntryByPuuidDto>();
        if (soloEntry is not null)
        {
            entries.Add(soloEntry);
        }

        if (extraEntries is not null)
        {
            entries.AddRange(extraEntries);
        }

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(Arg.Any<string>(), Arg.Any<RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(new RiotAccountDto
            {
                Puuid = callInfo.ArgAt<string>(0),
                GameName = "player",
                TagLine = "KR1"
            }));

        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient.GetLeagueEntriesByPuuidAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(entries));

        return BuildProcessWithClients(accountClient, platformClient);
    }

    private AccountRefreshProcess BuildProcessWithClients(
        IRiotAccountClient accountClient,
        IRiotPlatformClient platformClient)
        => new(
            NullLogger<AccountRefreshProcess>.Instance,
            accountClient,
            platformClient,
            _fixture.CreateSessionFactory(),
            new RankSnapshotWriter(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new AccountRefreshOptions { BatchSize = 200 }));

    private async Task<RiotAccount> SeedAccountAsync(string puuid)
    {
        await using var db = _fixture.CreateDbContext();
        var account = new RiotAccount
        {
            Puuid = puuid,
            PlatformId = Platform,
            GameName = "player",
            TagLine = "KR1",
            SummonerId = $"sum-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        db.RiotAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private async Task SeedSnapshotAsync(Guid riotAccountId, string tier, string division, int lp, DateTime capturedAtUtc)
    {
        await using var db = _fixture.CreateDbContext();
        db.RankSnapshots.Add(new RankSnapshot
        {
            Id = Guid.NewGuid(),
            RiotAccountId = riotAccountId,
            CapturedAtUtc = capturedAtUtc,
            Tier = tier,
            Division = division,
            LeaguePoints = lp,
            Wins = 5,
            Losses = 5
        });
        await db.SaveChangesAsync();
    }

    private static RiotLeagueEntryByPuuidDto SoloEntry(string tier, string division, int lp, int wins = 0, int losses = 0)
        => new()
        {
            QueueType = "RANKED_SOLO_5x5",
            Tier = tier,
            Rank = division,
            LeaguePoints = lp,
            Wins = wins,
            Losses = losses
        };
}
