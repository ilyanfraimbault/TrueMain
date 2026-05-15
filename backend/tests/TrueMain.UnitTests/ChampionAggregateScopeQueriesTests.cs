using Data.Entities;
using FluentAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionAggregateScopeQueriesTests
{
    private static readonly Guid AccountA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AccountB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void WhereChampionScope_AlwaysFiltersByChampionAndQueue()
    {
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP"),
            BuildScope(championId: 22, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP"),
            BuildScope(championId: 11, queueId: 440, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP")
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(championId: 11, queueId: 420, riotAccountId: null, patch: null, platformId: null, position: null)
            .ToList();

        matched.Should().HaveCount(1);
        matched[0].ChampionId.Should().Be(11);
        matched[0].QueueId.Should().Be(420);
    }

    [Fact]
    public void WhereChampionScope_AppliesEachOptionalFilter()
    {
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountB, version: "16.4", platform: "KR", position: "TOP"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.5", platform: "KR", position: "TOP"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "EUW1", position: "TOP"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "MIDDLE")
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(
                championId: 11,
                queueId: 420,
                riotAccountId: AccountA,
                patch: "16.4",
                platformId: "KR",
                position: "TOP")
            .ToList();

        matched.Should().ContainSingle();
        matched[0].RiotAccountId.Should().Be(AccountA);
        matched[0].GameVersion.Should().Be("16.4");
        matched[0].PlatformId.Should().Be("KR");
        matched[0].Position.Should().Be("TOP");
    }

    [Fact]
    public void WhereChampionScope_TreatsWhitespaceFiltersAsAbsent()
    {
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.5", platform: "KR", position: "TOP")
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(
                championId: 11,
                queueId: 420,
                riotAccountId: null,
                patch: "   ",
                platformId: "",
                position: null)
            .ToList();

        matched.Should().HaveCount(2);
    }

    private static ChampionAggregateScope BuildScope(
        int championId,
        int queueId,
        Guid accountId,
        string version,
        string platform,
        string position)
    {
        return new ChampionAggregateScope
        {
            Id = Guid.NewGuid(),
            ChampionId = championId,
            QueueId = queueId,
            RiotAccountId = accountId,
            GameVersion = version,
            PlatformId = platform,
            Position = position,
            Games = 1,
            Wins = 1
        };
    }
}
