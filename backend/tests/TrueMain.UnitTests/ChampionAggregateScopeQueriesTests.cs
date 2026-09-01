using Data.Entities;
using AwesomeAssertions;
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

    [Fact]
    public void WhereChampionScope_NullBracketSet_MatchesEveryBracket()
    {
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP", eloBracket: "GOLD"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP", eloBracket: "DIAMOND")
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(
                championId: 11, queueId: 420, riotAccountId: null, patch: null, platformId: null, position: null,
                eloBrackets: null)
            .ToList();

        matched.Should().HaveCount(2);
    }

    [Fact]
    public void WhereChampionScope_NonEmptyBracketSet_RestrictsToThoseBrackets()
    {
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP", eloBracket: "GOLD"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP", eloBracket: "DIAMOND")
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(
                championId: 11, queueId: 420, riotAccountId: null, patch: null, platformId: null, position: null,
                eloBrackets: ["GOLD"])
            .ToList();

        matched.Should().ContainSingle();
        matched[0].EloBracket.Should().Be("GOLD");
    }

    [Fact]
    public void WhereChampionScope_EmptyNonNullBracketSet_MatchesNothing()
    {
        // A rejected filter resolves to an empty, non-null set
        // (EloBracket.ResolveFilterOrEmpty) — it must match nothing, not fall
        // back to "every bracket" the way a null set does. Regression test for
        // the `is { Count: > 0 }` guard that treated the two alike.
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP", eloBracket: "GOLD"),
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP", eloBracket: "DIAMOND")
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(
                championId: 11, queueId: 420, riotAccountId: null, patch: null, platformId: null, position: null,
                eloBrackets: [])
            .ToList();

        matched.Should().BeEmpty();
    }

    [Fact]
    public void WhereChampionScope_KeepsOnlyMainsByDefault()
    {
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP"),
            BuildScope(
                championId: 11, queueId: 420, accountId: AccountB, version: "16.4", platform: "KR", position: "TOP",
                isMain: false)
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(championId: 11, queueId: 420, riotAccountId: null, patch: null, platformId: null, position: null)
            .ToList();

        // The default has to be the pre-#1346 population: every caller that does
        // not opt in keeps the numbers it has always returned.
        matched.Should().ContainSingle();
        matched[0].RiotAccountId.Should().Be(AccountA);
    }

    [Fact]
    public void WhereChampionScope_WidensToEveryPlayerWhenTruemainsOnlyIsOff()
    {
        var scopes = new[]
        {
            BuildScope(championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP"),
            BuildScope(
                championId: 11, queueId: 420, accountId: AccountB, version: "16.4", platform: "KR", position: "TOP",
                isMain: false)
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(
                championId: 11,
                queueId: 420,
                riotAccountId: null,
                patch: null,
                platformId: null,
                position: null,
                truemainsOnly: false)
            .ToList();

        // Off is a superset, not a swap: the mains are still in there.
        matched.Should().HaveCount(2);
        matched.Select(scope => scope.RiotAccountId).Should().Contain([AccountA, AccountB]);
    }

    [Fact]
    public void WhereChampionScope_CombinesTheTruemainsFilterWithTheBracketFilter()
    {
        var scopes = new[]
        {
            BuildScope(
                championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP",
                eloBracket: "MASTER"),
            BuildScope(
                championId: 11, queueId: 420, accountId: AccountB, version: "16.4", platform: "KR", position: "TOP",
                eloBracket: "MASTER", isMain: false),
            BuildScope(
                championId: 11, queueId: 420, accountId: AccountA, version: "16.4", platform: "KR", position: "TOP",
                eloBracket: "GOLD")
        };

        var matched = scopes.AsQueryable()
            .WhereChampionScope(
                championId: 11,
                queueId: 420,
                riotAccountId: null,
                patch: null,
                platformId: null,
                position: null,
                eloBrackets: ["MASTER"])
            .ToList();

        // Both narrow: a Master non-main and a Gold main are each excluded.
        matched.Should().ContainSingle();
        matched[0].RiotAccountId.Should().Be(AccountA);
        matched[0].EloBracket.Should().Be("MASTER");
    }

    private static ChampionAggregateScope BuildScope(
        int championId,
        int queueId,
        Guid accountId,
        string version,
        string platform,
        string position,
        string eloBracket = "GOLD",
        bool isMain = true)
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
            EloBracket = eloBracket,
            // Defaults to a main, matching the entity's own column default and
            // the only population that existed before #1346 — so every test that
            // isn't about the truemains filter reads as it always did.
            IsMain = isMain,
            Games = 1,
            Wins = 1
        };
    }
}
