using Core.Lol.Identifiers;
using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot.Dto;

namespace TrueMain.IntegrationTests;

public sealed class AccountUpsertIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public AccountUpsertIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpsertAsync_ShouldInsertNewRiotAccount()
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
        account.GameName.Should().Be("player-one");
        account.SummonerId.Should().Be("summoner-1");
        account.ProfileIconId.Should().Be(12);
        account.SummonerLevel.Should().Be(77);
        account.LastProfileSyncAtUtc.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task UpsertAsync_ShouldUpdateExistingRiotAccount()
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

        account.GameName.Should().Be("updated-player");
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
            GameName = "old-player",
            TagLine = null,
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
