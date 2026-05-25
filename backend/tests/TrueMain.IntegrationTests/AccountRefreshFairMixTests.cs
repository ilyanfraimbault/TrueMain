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
    public async Task GetAccountsForRefreshAsync_DrainsIncompleteTruemainsBeforeFairMix()
    {
        // Priority 0 (issue #188): truemains with an incomplete identity
        // (empty GameName or null TagLine) jump the 75/25 fair-mix and take
        // the whole batch if their pool is large enough. Complete-identity
        // truemains and non-truemains do not get refreshed until every
        // incomplete truemain is in flight.
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var now = DateTime.UtcNow;

            // 50 incomplete truemains — exactly the batch size we'll request.
            for (var i = 0; i < 50; i++)
            {
                var puuid = $"puuid-truemain-incomplete-{i:D3}";
                db.RiotAccounts.Add(NewAccount(puuid, gameName: "", tagLine: null, now.AddDays(-2).AddMinutes(i)));
                db.MainChampionStats.Add(NewMainStat(puuid));
            }

            // Plenty of complete truemains and non-truemains that would
            // normally win the fair-mix on UpdatedAtUtc — must NOT appear.
            for (var i = 0; i < 100; i++)
            {
                var truemainPuuid = $"puuid-truemain-complete-{i:D3}";
                db.RiotAccounts.Add(NewAccount(truemainPuuid, $"main-{i}", "KR1", now.AddDays(-1).AddMinutes(i)));
                db.MainChampionStats.Add(NewMainStat(truemainPuuid));

                var otherPuuid = $"puuid-other-{i:D3}";
                db.RiotAccounts.Add(NewAccount(otherPuuid, $"other-{i}", "KR1", now.AddDays(-1).AddMinutes(i)));
            }

            await db.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(verify);

        var batch = await repo.GetAccountsForRefreshAsync(50, CancellationToken.None);

        batch.Should().HaveCount(50);
        batch.Should().OnlyContain(
            a => a.Puuid.StartsWith("puuid-truemain-incomplete-"),
            "every slot should go to incomplete-identity truemains before any complete or non-truemain account is touched");
    }

    [Fact]
    public async Task GetAccountsForRefreshAsync_FillsRemainingCapacityWithFairMixAfterDrainingIncompleteTruemains()
    {
        // If priority 0 doesn't fill the batch, the remaining capacity
        // follows the legacy 75/25 truemain / non-truemain fair-mix. With
        // 10 incomplete truemains and a batch of 100, we expect 10 P0 picks
        // + (75 % of 90) ≈ 68 complete truemains + (25 % of 90) ≈ 22 others.
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var now = DateTime.UtcNow;

            for (var i = 0; i < 10; i++)
            {
                var puuid = $"puuid-truemain-incomplete-{i:D3}";
                db.RiotAccounts.Add(NewAccount(puuid, gameName: "", tagLine: null, now.AddDays(-2).AddMinutes(i)));
                db.MainChampionStats.Add(NewMainStat(puuid));
            }

            for (var i = 0; i < 200; i++)
            {
                var truemainPuuid = $"puuid-truemain-complete-{i:D3}";
                db.RiotAccounts.Add(NewAccount(truemainPuuid, $"main-{i}", "KR1", now.AddDays(-1).AddMinutes(i)));
                db.MainChampionStats.Add(NewMainStat(truemainPuuid));

                var otherPuuid = $"puuid-other-{i:D3}";
                db.RiotAccounts.Add(NewAccount(otherPuuid, $"other-{i}", "KR1", now.AddDays(-1).AddMinutes(i)));
            }

            await db.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(verify);

        var batch = await repo.GetAccountsForRefreshAsync(100, CancellationToken.None);

        batch.Should().HaveCount(100);

        var incompletePicked = batch.Count(a => a.Puuid.StartsWith("puuid-truemain-incomplete-"));
        var completeTruemainPicked = batch.Count(a => a.Puuid.StartsWith("puuid-truemain-complete-"));
        var otherPicked = batch.Count(a => a.Puuid.StartsWith("puuid-other-"));

        incompletePicked.Should().Be(10, "all 10 incomplete truemains are picked first");
        completeTruemainPicked.Should().Be(68, "75 % of the 90 leftover slots go to complete truemains (ceil(90 * 0.75))");
        otherPicked.Should().Be(22, "the remaining 22 slots go to non-truemains");
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
