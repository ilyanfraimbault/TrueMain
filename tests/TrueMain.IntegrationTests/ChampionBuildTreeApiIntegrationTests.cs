using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TrueMain.Contracts.Champions;

namespace TrueMain.IntegrationTests;

public sealed class ChampionBuildTreeApiIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public ChampionBuildTreeApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionAsync_ShouldEmbedAggregateBackedTree()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBuildTreeAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionResponse>(
            "/champions/67?patch=16.5&platformId=KR&position=BOTTOM&maxDepth=3&minBranchGames=1");

        payload.Should().NotBeNull();
        payload!.BuildTree.ChampionId.Should().Be(67);
        payload.BuildTree.Patch.Should().Be("16.5");
        payload.BuildTree.Position.Should().Be("BOTTOM");
        payload.BuildTree.TotalGames.Should().Be(10);
        payload.BuildTree.Build.Should().HaveCount(1);
        payload.BuildTree.Build[0].ItemId.Should().Be(3153);
        payload.BuildTree.Build[0].Games.Should().Be(10);
        payload.BuildTree.Build[0].PickRate.Should().BeApproximately(1.0, 0.0001);
        payload.BuildTree.Build[0].Children.Should().HaveCount(2);
        payload.BuildTree.Build[0].Children[0].ItemId.Should().Be(3006);
        payload.BuildTree.Build[0].Children[0].PickRate.Should().BeApproximately(0.7, 0.0001);
        payload.BuildTree.Build[0].Children[1].ItemId.Should().Be(3091);
        payload.BuildTree.Build[0].Children[1].PickRate.Should().BeApproximately(0.3, 0.0001);
        payload.Core.BuildPath.Should().NotBeNull();
        payload.Core.BuildPath!.ItemIds.Should().Equal(3153, 3006, 6672);
    }

    [Fact]
    public async Task GetChampionAsync_ShouldFilterByRiotAccountIdAndIgnoreNullOptionalFilters()
    {
        await _fixture.ResetDatabaseAsync();
        var (account1Id, _) = await SeedBuildTreeAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionResponse>(
            $"/champions/67?riotAccountId={account1Id}&patch=&platformId=&position=&maxDepth=3&minBranchGames=1");

        payload.Should().NotBeNull();
        payload!.BuildTree.RiotAccountId.Should().Be(account1Id);
        payload.BuildTree.Patch.Should().BeNull();
        payload.BuildTree.Position.Should().BeNull();
        payload.BuildTree.PlatformId.Should().BeNull();
        payload.BuildTree.TotalGames.Should().Be(9);
        payload.BuildTree.Build.Should().HaveCount(2);
        payload.BuildTree.Build[0].ItemId.Should().Be(3153);
        payload.BuildTree.Build[0].Children.Single().ItemId.Should().Be(3006);
    }

    [Fact]
    public async Task GetChampionAsync_ShouldReturnEmptyEmbeddedBuild_WhenNoRowsMatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBuildTreeAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/67?patch=99.1&position=BOTTOM");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ChampionResponse>();
        payload.Should().NotBeNull();
        payload!.BuildTree.TotalGames.Should().Be(0);
        payload.BuildTree.Build.Should().BeEmpty();
        payload.Core.BuildPath.Should().BeNull();
    }

    [Fact]
    public async Task GetChampionAsync_ShouldExcludeEmptyBuildRowsFromEmbeddedBuildTotalGames()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBuildTreeAggregatesWithEmptyBuildAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionResponse>(
            "/champions/67?patch=16.5&platformId=KR&position=BOTTOM&maxDepth=3&minBranchGames=1");

        payload.Should().NotBeNull();
        payload!.BuildTree.TotalGames.Should().Be(10);
        payload.BuildTree.Build.Should().ContainSingle();
        payload.BuildTree.Build[0].PickRate.Should().BeApproximately(1.0, 0.0001);
    }

    private async Task<(Guid Account1Id, Guid Account2Id)> SeedBuildTreeAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var account2Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "tree-puuid-1", "tree-one", now),
            BuildAccount(account2Id, "tree-puuid-2", "tree-two", now));

        db.ChampionPatternAggregates.AddRange(
            BuildAggregate(account1Id, 67, "16.5", "KR", 420, "BOTTOM", [3153, 3006, 6672], 7, 4, now.AddMinutes(-10)),
            BuildAggregate(account2Id, 67, "16.5", "KR", 420, "BOTTOM", [3153, 3091, 3085], 3, 2, now.AddMinutes(-8)),
            BuildAggregate(account2Id, 67, "16.4", "KR", 420, "BOTTOM", [6672, 3006, 3031], 5, 2, now.AddDays(-1)),
            BuildAggregate(account1Id, 67, "16.5", "EUW", 420, "BOTTOM", [6672, 3006, 3031], 2, 1, now.AddMinutes(-7)),
            BuildAggregate(account1Id, 67, "16.5", "KR", 450, "BOTTOM", [6672, 3006, 3031], 4, 3, now.AddMinutes(-6)));

        await db.SaveChangesAsync();
        return (account1Id, account2Id);
    }

    private async Task SeedBuildTreeAggregatesWithEmptyBuildAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("33333333-4444-5555-6666-777777777777");
        var account2Id = Guid.Parse("88888888-9999-aaaa-bbbb-cccccccccccc");

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "tree-empty-puuid-1", "tree-empty-one", now),
            BuildAccount(account2Id, "tree-empty-puuid-2", "tree-empty-two", now));

        db.ChampionPatternAggregates.AddRange(
            BuildAggregate(account1Id, 67, "16.5", "KR", 420, "BOTTOM", [3153, 3006, 6672], 7, 4, now.AddMinutes(-10)),
            BuildAggregate(account2Id, 67, "16.5", "KR", 420, "BOTTOM", [3153, 3091, 3085], 3, 2, now.AddMinutes(-8)),
            BuildAggregate(account2Id, 67, "16.5", "KR", 420, "BOTTOM", [], 5, 1, now.AddMinutes(-7)));

        await db.SaveChangesAsync();
    }

    private static RiotAccount BuildAccount(Guid id, string puuid, string gameName, DateTime now)
        => new()
        {
            Id = id,
            PlatformId = "KR",
            Puuid = puuid,
            GameName = gameName,
            SummonerId = $"{gameName}-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1)
        };

    private static ChampionPatternAggregate BuildAggregate(
        Guid riotAccountId,
        int championId,
        string patch,
        string platformId,
        int queueId,
        string position,
        IReadOnlyList<int> buildItems,
        int games,
        int wins,
        DateTime aggregatedAtUtc)
    {
        var build = buildItems.Concat(Enumerable.Repeat(0, 7)).Take(7).ToArray();

        return new ChampionPatternAggregate
        {
            RiotAccountId = riotAccountId,
            ChampionId = championId,
            GameVersion = patch,
            PlatformId = platformId,
            QueueId = queueId,
            Position = position,
            PrimaryStyleId = 8000,
            SubStyleId = 8200,
            PerksOffense = 5005,
            PerksFlex = 5008,
            PerksDefense = 5002,
            SummonerSpell1Id = 4,
            SummonerSpell2Id = 7,
            SkillOrderKey = "Q-W-E",
            StarterItems = [1055, 2003],
            StarterItemsKey = "1055-2003",
            BuildItem0 = build[0],
            BuildItem1 = build[1],
            BuildItem2 = build[2],
            BuildItem3 = build[3],
            BuildItem4 = build[4],
            BuildItem5 = build[5],
            BuildItem6 = build[6],
            Games = games,
            Wins = wins,
            LastGameStartTimeUtc = aggregatedAtUtc.AddMinutes(-30),
            AggregatedAtUtc = aggregatedAtUtc
        };
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("ConnectionStrings:TrueMain", fixture.ConnectionString),
                    new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")
                ]);
            });
        }
    }
}
