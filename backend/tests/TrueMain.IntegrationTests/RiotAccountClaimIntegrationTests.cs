using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class RiotAccountClaimIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public RiotAccountClaimIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClaimAccountsForMatchIngestAtomicallyAsync_ShouldClaimDisjointAccountsAcrossParallelWorkers()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedClaimableAccountsAsync(4, MatchIngestStatus.Idle, null);

        var now = DateTime.UtcNow;
        var lease = TimeSpan.FromMinutes(30);

        var task1 = ClaimAsync(now, lease);
        var task2 = ClaimAsync(now, lease);

        await Task.WhenAll(task1, task2);

        var claimed1 = await task1;
        var claimed2 = await task2;

        claimed1.Intersect(claimed2).Should().BeEmpty();
        (claimed1.Count + claimed2.Count).Should().Be(4);

        await using var verifyDb = _fixture.CreateDbContext();
        verifyDb.RiotAccounts.Count(account => account.MatchIngestStatus == MatchIngestStatus.Processing).Should().Be(4);
    }

    [Fact]
    public async Task ClaimAccountsForMatchIngestAtomicallyAsync_ShouldReclaimExpiredProcessingAccounts()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedClaimableAccountsAsync(1, MatchIngestStatus.Processing, now.AddHours(-2));

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var claimed = await repo.ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = 1 },
            1,
            new Dictionary<string, double> { ["KR"] = 0.7 },
            now,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        claimed.Should().ContainSingle();
        claimed[0].PlatformId.Should().Be("KR");
    }

    [Fact]
    public async Task ClaimAccountsForMatchIngestAtomicallyAsync_ShouldGiveMostOfTheBatchToEstablishedMains()
    {
        // #900: depth over breadth. Eight accounts compete for a batch of four — four are
        // active established mains, four are brand-new queued candidates. With a 0.75 share
        // three of the four slots must go to the mains, whatever their ingest recency.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedEstablishedMainsAsync(4, lastMatchIngestAtUtc: now.AddHours(-1));
        await SeedClaimableAccountsAsync(4, MatchIngestStatus.Idle, null);

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var claimed = await repo.ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = 4 },
            4,
            new Dictionary<string, double> { ["KR"] = 0.75 },
            now,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        claimed.Should().HaveCount(4);
        claimed.Count(key => key.Puuid.StartsWith("puuid-main-", StringComparison.Ordinal)).Should().Be(3);
    }

    [Fact]
    public async Task ClaimAccountsForMatchIngestAtomicallyAsync_ShouldApplyEachPlatformItsOwnEstablishedShare()
    {
        // #1533: a saturated platform spends on depth, a thin one on breadth. Both platforms
        // offer four established mains and four queued candidates for a quota of four; the
        // single scalar this replaced (their mean, 0.5) would have split both 2/2.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        foreach (var platform in new[] { "KR", "EUW1" })
        {
            await SeedEstablishedMainsAsync(4, lastMatchIngestAtUtc: now.AddHours(-1), platformId: platform);
            await SeedClaimableAccountsAsync(4, MatchIngestStatus.Idle, null, platform);
        }

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var claimed = await repo.ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = 4, ["EUW1"] = 4 },
            8,
            new Dictionary<string, double> { ["KR"] = 0.75, ["EUW1"] = 0.25 },
            now,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        claimed.Should().HaveCount(8);
        int MainsOn(string platform) => claimed.Count(key =>
            key.PlatformId == platform && key.Puuid.StartsWith("puuid-main-", StringComparison.Ordinal));
        MainsOn("KR").Should().Be(3);
        MainsOn("EUW1").Should().Be(1);
    }

    [Fact]
    public async Task ClaimAccountsForMatchIngestAtomicallyAsync_ShouldSkipInactiveMains()
    {
        // An inactive main must stop consuming match-v5 calls entirely (#900); with no
        // queued candidate to fall back on, the claim comes back empty.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedEstablishedMainsAsync(2, lastMatchIngestAtUtc: now.AddDays(-5), isActive: false);

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var claimed = await repo.ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = 10 },
            10,
            new Dictionary<string, double> { ["KR"] = 0.7 },
            now,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimAccountsForMatchIngestAtomicallyAsync_ShouldSpillTheMainQuota_WhenNoMainIsClaimable()
    {
        // The share is a floor, not a partition: with no established main to serve, the
        // whole batch still goes to queued candidates rather than staying idle.
        await _fixture.ResetDatabaseAsync();
        await SeedClaimableAccountsAsync(3, MatchIngestStatus.Idle, null);

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var claimed = await repo.ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = 3 },
            3,
            new Dictionary<string, double> { ["KR"] = 0.7 },
            DateTime.UtcNow,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        claimed.Should().HaveCount(3);
    }

    private async Task SeedEstablishedMainsAsync(
        int count,
        DateTime lastMatchIngestAtUtc,
        bool isActive = true,
        string platformId = "KR")
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= count; i++)
        {
            var puuid = platformId == "KR" ? $"puuid-main-{i}" : $"puuid-main-{platformId}-{i}";
            db.RiotAccounts.Add(new RiotAccount
            {
                Puuid = puuid,
                PlatformId = platformId,
                GameName = $"main-{i}",
                TagLine = platformId,
                SummonerId = $"sum-main-{i}",
                ProfileIconId = 1,
                SummonerLevel = 300,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastMatchIngestAtUtc = lastMatchIngestAtUtc,
                MatchIngestStatus = MatchIngestStatus.Idle
            });

            db.MainChampionStats.Add(new MainChampionStat
            {
                PlatformId = platformId,
                Puuid = puuid,
                ChampionId = 22,
                TotalMatches = 30,
                ChampionMatches = 25,
                PlayRate = 0.83,
                IsMain = true,
                IsActive = isActive,
                IsOtp = false,
                PrimaryPosition = "BOTTOM",
                PositionBreakdown = [],
                CalculatedAtUtc = now
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedClaimableAccountsAsync(
        int count,
        MatchIngestStatus status,
        DateTime? claimedAtUtc,
        string platformId = "KR")
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= count; i++)
        {
            var puuid = platformId == "KR" ? $"puuid-{i}" : $"puuid-{platformId}-{i}";
            db.RiotAccounts.Add(new RiotAccount
            {
                Puuid = puuid,
                PlatformId = platformId,
                GameName = $"player-{i}",
                TagLine = platformId,
                SummonerId = $"sum-{i}",
                ProfileIconId = 1,
                SummonerLevel = 100,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                MatchIngestStatus = status,
                MatchIngestClaimedAtUtc = claimedAtUtc
            });

            db.MainCandidates.Add(new MainCandidate
            {
                PlatformId = platformId,
                Puuid = puuid,
                ChampionId = 1,
                ChampionRankInMasteryTop = 1,
                ChampionPoints = 1000,
                LastPlayTimeUtc = now,
                DiscoveredAtUtc = now,
                Status = MainCandidateStatus.Queued
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<List<AccountKey>> ClaimAsync(DateTime nowUtc, TimeSpan lease)
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);
        return await repo.ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = 2 }, 2, new Dictionary<string, double> { ["KR"] = 0.7 }, nowUtc, lease, CancellationToken.None);
    }
}
