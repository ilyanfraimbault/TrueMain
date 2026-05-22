using Data.Entities;
using Data.Repositories;
using FluentAssertions;

namespace TrueMain.IntegrationTests;

public sealed class AccountRefreshFairMixTests : IClassFixture<PostgresFixture>
{
    private const string Platform = "KR";

    private readonly PostgresFixture _fixture;

    public AccountRefreshFairMixTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAccountsForRefreshAsync_WithFullBuckets_Returns75PercentTruemains()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountsAsync(truemainCount: 200, otherCount: 200);

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var batch = await repo.GetAccountsForRefreshAsync(200, CancellationToken.None);

        var truemainPuuids = await TruemainPuuidsAsync();
        var truemainsPicked = batch.Count(a => truemainPuuids.Contains(a.Puuid));

        batch.Should().HaveCount(200);
        truemainsPicked.Should().Be(150, "75% of the batch should be truemains when supply allows");
    }

    [Fact]
    public async Task GetAccountsForRefreshAsync_WithFewTruemains_RebalancesUnderflow()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountsAsync(truemainCount: 50, otherCount: 500);

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var batch = await repo.GetAccountsForRefreshAsync(200, CancellationToken.None);

        var truemainPuuids = await TruemainPuuidsAsync();
        var truemainsPicked = batch.Count(a => truemainPuuids.Contains(a.Puuid));

        batch.Should().HaveCount(200);
        truemainsPicked.Should().Be(50, "all available truemains should be picked");
        (batch.Count - truemainsPicked).Should().Be(150, "non-truemain bucket absorbs the underflow");
    }

    [Fact]
    public async Task GetAccountsForRefreshAsync_WithOnlyTruemains_ReturnsThemAll()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountsAsync(truemainCount: 100, otherCount: 0);

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var batch = await repo.GetAccountsForRefreshAsync(200, CancellationToken.None);

        batch.Should().HaveCount(100);
        var truemainPuuids = await TruemainPuuidsAsync();
        batch.All(a => truemainPuuids.Contains(a.Puuid)).Should().BeTrue();
    }

    [Fact]
    public async Task GetAccountsForRefreshAsync_WithNoTruemains_ReturnsOnlyOthers()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountsAsync(truemainCount: 0, otherCount: 300);

        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        var batch = await repo.GetAccountsForRefreshAsync(200, CancellationToken.None);

        batch.Should().HaveCount(200);
        var truemainPuuids = await TruemainPuuidsAsync();
        batch.All(a => !truemainPuuids.Contains(a.Puuid)).Should().BeTrue();
    }

    [Fact]
    public async Task GetAccountsForRefreshAsync_PrioritizesIncompleteIdentityWithinBucket()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var now = DateTime.UtcNow;

            // Old but complete — should come after the incomplete one within the truemain bucket.
            var oldComplete = NewAccount("puuid-truemain-old-complete", "complete-name", "TAG", now.AddDays(-30));
            // Recent but missing TagLine — should be picked first.
            var recentIncomplete = NewAccount("puuid-truemain-recent-incomplete", "incomplete-name", null, now.AddMinutes(-5));

            db.RiotAccounts.AddRange(oldComplete, recentIncomplete);
            db.MainChampionStats.AddRange(
                NewMainStat(oldComplete.Puuid),
                NewMainStat(recentIncomplete.Puuid));

            await db.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(verify);

        var batch = await repo.GetAccountsForRefreshAsync(2, CancellationToken.None);

        batch.Should().HaveCount(2);
        batch[0].Puuid.Should().Be("puuid-truemain-recent-incomplete",
            "accounts with incomplete identity come before stale-but-complete ones");
    }

    private async Task SeedAccountsAsync(int truemainCount, int otherCount)
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var staleness = TimeSpan.FromMinutes(1);

        for (var i = 0; i < truemainCount; i++)
        {
            var puuid = $"puuid-truemain-{i:D4}";
            db.RiotAccounts.Add(NewAccount(puuid, $"main-{i}", "KR1", now.AddDays(-1).Add(i * staleness)));
            db.MainChampionStats.Add(NewMainStat(puuid));
        }

        for (var i = 0; i < otherCount; i++)
        {
            var puuid = $"puuid-other-{i:D4}";
            db.RiotAccounts.Add(NewAccount(puuid, $"other-{i}", "KR1", now.AddDays(-1).Add(i * staleness)));
        }

        await db.SaveChangesAsync();
    }

    private async Task<HashSet<string>> TruemainPuuidsAsync()
    {
        await using var db = _fixture.CreateDbContext();
        var puuids = db.MainChampionStats.Where(s => s.IsMain).Select(s => s.Puuid).ToList();
        await Task.Yield();
        return puuids.ToHashSet();
    }

    private static RiotAccount NewAccount(string puuid, string gameName, string? tagLine, DateTime updatedAtUtc)
        => new()
        {
            Puuid = puuid,
            PlatformId = Platform,
            GameName = gameName,
            TagLine = tagLine,
            SummonerId = $"sum-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = updatedAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };

    private static MainChampionStat NewMainStat(string puuid)
        => new()
        {
            PlatformId = Platform,
            Puuid = puuid,
            ChampionId = 1,
            TotalMatches = 100,
            ChampionMatches = 70,
            PlayRate = 0.7,
            IsMain = true,
            PrimaryPosition = "MIDDLE",
            CalculatedAtUtc = DateTime.UtcNow
        };
}
