using Data.Entities;
using Data.Repositories;
using FluentAssertions;

namespace TrueMain.IntegrationTests;

public sealed class RiotAccountClaimIntegrationTests : IClassFixture<PostgresFixture>
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
            new[] { "KR" },
            1,
            now,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        claimed.Should().ContainSingle();
        claimed[0].PlatformId.Should().Be("KR");
    }

    private async Task SeedClaimableAccountsAsync(int count, MatchIngestStatus status, DateTime? claimedAtUtc)
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= count; i++)
        {
            var puuid = $"puuid-{i}";
            db.RiotAccounts.Add(new RiotAccount
            {
                Puuid = puuid,
                PlatformId = "KR",
                GameName = $"player-{i}",
                TagLine = "KR1",
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
                PlatformId = "KR",
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
        return await repo.ClaimAccountsForMatchIngestAtomicallyAsync(new[] { "KR" }, 2, nowUtc, lease, CancellationToken.None);
    }
}
