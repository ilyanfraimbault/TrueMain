using Core.Lol.Identifiers;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot.Dto;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class AccountUpsertIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public AccountUpsertIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpsertAsync_ShouldInsertNewRiotAccount_WithEmptyIdentityForAccountRefreshToFill()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var service = new AccountUpsertService();

        var result = await service.UpsertAsync(
            session,
            PlatformRoute.KR,
            new RiotSummonerDto
            {
                Id = "summoner-1",
                Puuid = "puuid-1",
                Name = "player-one",
                ProfileIconId = 12,
                SummonerLevel = 77
            },
            now,
            CancellationToken.None);

        result.IsNew.Should().BeTrue();
        result.Account.Puuid.Should().Be("puuid-1");
        await session.SaveChangesAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-1");

        account.PlatformId.Should().Be("KR");
        // GameName / TagLine are owned by account-v1 (resolved by
        // AccountRefreshProcess), so Discovery's upsert MUST leave them at
        // the entity defaults — even when the summoner-v4 payload still
        // carries a non-empty Name field, that value is not authoritative.
        account.GameName.Should().BeEmpty();
        account.TagLine.Should().BeNull();
        account.SummonerId.Should().Be("summoner-1");
        account.ProfileIconId.Should().Be(12);
        account.SummonerLevel.Should().Be(77);
        account.LastProfileSyncAtUtc.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task UpsertAsync_ShouldUpdateExistingRiotAccount_PreservingAccountRefreshIdentity()
    {
        await _fixture.ResetDatabaseAsync();
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var now = DateTime.UtcNow;

        await SeedAccountAsync(createdAt);

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var service = new AccountUpsertService();

        var result = await service.UpsertAsync(
            session,
            PlatformRoute.KR,
            new RiotSummonerDto
            {
                Id = "summoner-2",
                Puuid = "puuid-1",
                Name = "updated-player",
                ProfileIconId = 99,
                SummonerLevel = 300
            },
            now,
            CancellationToken.None);

        result.IsNew.Should().BeFalse();
        result.Account.Puuid.Should().Be("puuid-1");
        await session.SaveChangesAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-1");

        // GameName / TagLine are owned by AccountRefreshProcess via account-v1.
        // The seeded values must survive a Discovery upsert — otherwise Discovery
        // and AccountRefresh enter a clobber loop that leaves the leaderboard
        // without names. See issue #182.
        account.GameName.Should().Be("seeded-player");
        account.TagLine.Should().Be("EUW");
        account.SummonerId.Should().Be("summoner-2");
        account.ProfileIconId.Should().Be(99);
        account.SummonerLevel.Should().Be(300);
        account.UpdatedAtUtc.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
        account.LastProfileSyncAtUtc.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
        account.CreatedAtUtc.Should().BeCloseTo(createdAt, TimeSpan.FromMilliseconds(1));
    }

    private async Task SeedAccountAsync(DateTime createdAtUtc)
    {
        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = "puuid-1",
            GameName = "seeded-player",
            TagLine = "EUW",
            PlatformId = "KR",
            SummonerId = "summoner-old",
            ProfileIconId = 1,
            SummonerLevel = 10,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        });

        await db.SaveChangesAsync();
    }
}
