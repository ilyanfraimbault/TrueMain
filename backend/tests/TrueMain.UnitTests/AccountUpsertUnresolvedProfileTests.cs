using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot.Dto;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// When discovery skips summoner-v4 (#1358) the "summoner" it hands the upsert is a PUUID lifted
/// from the ladder entry, not a response. Writing it as one would erase the cosmetics and, worse,
/// re-stamp LastProfileSyncAtUtc — the freshness gate would then keep the row fresh for ever off
/// a call that never happened.
/// </summary>
public sealed class AccountUpsertUnresolvedProfileTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpsertAsync_WithAnUnresolvedProfile_LeavesTheCosmeticsAndTheSyncStampAlone()
    {
        var lastSync = NowUtc.AddDays(-2);
        var existing = new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = "puuid-a",
            PlatformId = "KR",
            SummonerId = "summoner-a",
            ProfileIconId = 23,
            SummonerLevel = 201,
            UpdatedAtUtc = NowUtc.AddDays(-2),
            LastProfileSyncAtUtc = lastSync
        };

        var result = await new AccountUpsertService().UpsertAsync(
            BuildSession(existing),
            PlatformId.Parse("KR").Route,
            new RiotSummonerDto { Puuid = "puuid-a" },
            NowUtc,
            CancellationToken.None,
            profileResolved: false);

        result.IsNew.Should().BeFalse();
        existing.SummonerId.Should().Be("summoner-a");
        existing.ProfileIconId.Should().Be(23);
        existing.SummonerLevel.Should().Be(201);
        existing.LastProfileSyncAtUtc.Should().Be(lastSync);
        existing.UpdatedAtUtc.Should().Be(NowUtc, "the row was still seen on the ladder this run");
    }

    [Fact]
    public async Task UpsertAsync_WithAResolvedProfile_StillWritesTheCosmetics()
    {
        var existing = new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = "puuid-a",
            PlatformId = "KR",
            SummonerId = "old-summoner",
            ProfileIconId = 1,
            SummonerLevel = 30,
            LastProfileSyncAtUtc = NowUtc.AddDays(-30)
        };

        await new AccountUpsertService().UpsertAsync(
            BuildSession(existing),
            PlatformId.Parse("KR").Route,
            new RiotSummonerDto { Id = "new-summoner", Puuid = "puuid-a", ProfileIconId = 42, SummonerLevel = 300 },
            NowUtc,
            CancellationToken.None);

        existing.SummonerId.Should().Be("new-summoner");
        existing.ProfileIconId.Should().Be(42);
        existing.SummonerLevel.Should().Be(300);
        existing.LastProfileSyncAtUtc.Should().Be(NowUtc);
    }

    private static IDataSession BuildSession(RiotAccount existing)
    {
        var accounts = Substitute.For<IRiotAccountRepository>();
        accounts.GetByPuuidAsync(existing.Puuid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RiotAccount?>(existing));

        var session = Substitute.For<IDataSession>();
        session.RiotAccounts.Returns(accounts);
        return session;
    }
}
