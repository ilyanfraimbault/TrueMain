using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TrueMain.ReadModels.Champions;

namespace TrueMain.IntegrationTests;

public sealed class ChampionFoundationApiIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public ChampionFoundationApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldReturnChampionFoundationContractsFromAggregates()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionFoundationAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/22");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(["summary", "core", "advanced", "buildTree"]);

        var summaryProperties = root.GetProperty("summary").EnumerateObject().Select(property => property.Name);
        summaryProperties.Should().BeEquivalentTo(
            ["championId", "games", "winRate", "trueMainCount", "position", "latestPatchVersion", "lastUpdatedAtUtc"]);

        var coreProperties = root.GetProperty("core").EnumerateObject().Select(property => property.Name);
        coreProperties.Should().BeEquivalentTo(
            ["sampleSize", "starterItems", "boots", "buildPath", "summonerSpells", "skillOrder"]);

        var advancedProperties = root.GetProperty("advanced").EnumerateObject().Select(property => property.Name);
        advancedProperties.Should().BeEquivalentTo(
            ["starterItemOptions", "summonerSpellOptions", "skillOrderOptions"]);

        root.GetProperty("core").GetProperty("summonerSpells").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["spell1Id", "spell2Id", "games", "playRate", "winRate"]);
        root.GetProperty("core").GetProperty("skillOrder").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["sequence", "games", "playRate", "winRate"]);
        root.GetProperty("core").GetProperty("starterItems").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["itemIds", "games", "playRate", "winRate"]);
        root.GetProperty("core").GetProperty("boots").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["itemIds", "games", "playRate", "winRate"]);
        root.GetProperty("core").GetProperty("buildPath").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["itemIds"]);
        AssertObjectArrayElementsHaveProperties(root.GetProperty("advanced").GetProperty("starterItemOptions"),
            "itemIds", "games", "playRate", "winRate");
        AssertObjectArrayElementsHaveProperties(root.GetProperty("advanced").GetProperty("summonerSpellOptions"),
            "spell1Id", "spell2Id", "games", "playRate", "winRate");
        AssertObjectArrayElementsHaveProperties(root.GetProperty("advanced").GetProperty("skillOrderOptions"),
            "sequence", "games", "playRate", "winRate");
        root.GetProperty("buildTree").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["championId", "patch", "position", "riotAccountId", "platformId", "totalGames", "boots", "build"]);

        var payload = await response.Content.ReadFromJsonAsync<ChampionReadModel>();
        payload.Should().NotBeNull();
        payload!.Summary.ChampionId.Should().Be(22);
        payload.Summary.Games.Should().Be(3);
        payload.Summary.TrueMainCount.Should().Be(2);
        payload.Summary.Position.Should().Be("BOTTOM");
        payload.Summary.LatestPatchVersion.Should().Be("16.4");
        payload.Core.SampleSize.Should().Be(3);
        payload.Core.StarterItems.Should().NotBeNull();
        payload.Core.StarterItems!.ItemIds.Should().Equal(1055, 2003);
        payload.Core.Boots.Should().NotBeNull();
        payload.Core.Boots!.ItemIds.Should().Equal(3006);
        payload.Core.BuildPath.Should().NotBeNull();
        payload.Core.BuildPath!.ItemIds.Should().Equal(6672, 3006, 3094);
        payload.Core.SummonerSpells.Should().NotBeNull();
        payload.Core.SummonerSpells!.Spell1Id.Should().Be(4);
        payload.Core.SummonerSpells.Spell2Id.Should().Be(7);
        payload.Core.SummonerSpells.Games.Should().Be(3);
        payload.Core.SkillOrder.Should().NotBeNull();
        payload.Core.SkillOrder!.Sequence.Should().Equal("Q", "W", "E");
        payload.Core.SkillOrder.Games.Should().Be(3);
        payload.Advanced.SummonerSpellOptions.Should().OnlyContain(option => option.Spell1Id == 4 && option.Spell2Id == 7);
        payload.Advanced.StarterItemOptions.Should().OnlyContain(option => option.ItemIds.Contains(1055));
        payload.BuildTree.ChampionId.Should().Be(22);
        payload.BuildTree.Patch.Should().Be("16.4");
        payload.BuildTree.TotalGames.Should().Be(3);
        payload.BuildTree.Boots.Should().NotBeNull();
        payload.BuildTree.Boots!.ItemIds.Should().Equal(3006);
        payload.BuildTree.Build.Should().NotBeEmpty();
        payload.BuildTree.Build[0].ItemId.Should().Be(6672);
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldUseLatestPatchAndDeterministicTieBreakers()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionFoundationTieAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionReadModel>("/champions/55");

        payload.Should().NotBeNull();
        payload!.Summary.Games.Should().Be(4);
        payload.Summary.LatestPatchVersion.Should().Be("16.5");
        payload.Summary.Position.Should().Be("MIDDLE");
        payload.Core.SampleSize.Should().Be(4);
        payload.Core.BuildPath.Should().NotBeNull();
        payload.Core.BuildPath!.ItemIds.Should().ContainInOrder(3006);
        payload.Core.SummonerSpells.Should().NotBeNull();
        payload.Core.SummonerSpells!.Spell1Id.Should().Be(4);
        payload.Core.SummonerSpells.Spell2Id.Should().Be(7);
        payload.Core.SkillOrder.Should().NotBeNull();
        payload.Core.SkillOrder!.Sequence.Should().Equal("Q", "W", "E");
        payload.BuildTree.Build.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldKeepCoreBlocksFromTheSameCorrelatedPattern()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionFoundationCorrelationAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionReadModel>("/champions/81");

        payload.Should().NotBeNull();
        payload!.Core.SummonerSpells.Should().NotBeNull();
        payload.Core.BuildPath.Should().NotBeNull();
        payload.Core.BuildPath!.ItemIds.Should().Equal(3153, 3006, 3091);
        payload.Core.SummonerSpells!.Spell1Id.Should().Be(4);
        payload.Core.SummonerSpells.Spell2Id.Should().Be(7);
        payload.Core.SkillOrder.Should().NotBeNull();
        payload.Core.SkillOrder!.Sequence.Should().Equal("Q", "W", "E");
        payload.BuildTree.Build.Should().NotBeEmpty();
        payload.BuildTree.Boots.Should().NotBeNull();
        payload.BuildTree.Boots!.ItemIds.Should().Equal(3006);
        payload.BuildTree.Build[0].ItemId.Should().Be(3153);
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldChooseTheFirstCorrelatedPatternWithABuildForCoreBlocks()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionFoundationWithEmptyTopPatternAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionReadModel>("/champions/110");

        payload.Should().NotBeNull();
        payload!.Core.SummonerSpells.Should().NotBeNull();
        payload.Core.BuildPath.Should().NotBeNull();
        payload.Core.BuildPath!.ItemIds.Should().Equal(3153, 3006, 3091);
        payload.Core.SummonerSpells!.Spell1Id.Should().Be(4);
        payload.Core.SummonerSpells.Spell2Id.Should().Be(7);
        payload.Core.SkillOrder.Should().NotBeNull();
        payload.Core.SkillOrder!.Sequence.Should().Equal("Q", "W", "E");
        payload.BuildTree.Build.Should().ContainSingle();
        payload.BuildTree.Boots.Should().NotBeNull();
        payload.BuildTree.Boots!.ItemIds.Should().Equal(3006);
        payload.BuildTree.Build[0].ItemId.Should().Be(3153);
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldReturnNotFound_WhenChampionDataDoesNotExist()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task SeedChampionFoundationAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var account2Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "KR", "foundation-puuid-1", "foundation-one", now),
            BuildAccount(account2Id, "KR", "foundation-puuid-2", "foundation-two", now));

        db.ChampionPatternAggregates.AddRange(
            BuildAggregate(account1Id, 22, "16.4", "KR", 420, "BOTTOM", 4, 7, "Q-W-E", [6672, 3006, 3094], 3006, 2, 1, now.AddMinutes(-10)),
            BuildAggregate(account2Id, 22, "16.4", "KR", 420, "BOTTOM", 7, 4, "Q-W-E", [6672, 3006, 3094], 3006, 1, 1, now.AddMinutes(-5)),
            BuildAggregate(account2Id, 22, "16.3", "KR", 420, "BOTTOM", 4, 7, "Q-E-W", [6672, 3085, 3031], 3111, 5, 2, now.AddDays(-2)),
            BuildAggregate(account1Id, 22, "16.9", "KR", 450, "BOTTOM", 4, 7, "Q-W-E", [6672, 3094, 3031], 3006, 3, 2, now.AddMinutes(-1)));

        await db.SaveChangesAsync();
    }

    private async Task SeedChampionFoundationTieAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var account2Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "KR", "tie-foundation-puuid-1", "tie-one", now),
            BuildAccount(account2Id, "KR", "tie-foundation-puuid-2", "tie-two", now));

        db.ChampionPatternAggregates.AddRange(
            BuildAggregate(account1Id, 55, "16.5", "KR", 420, "MIDDLE", 4, 7, "Q-W-E", [3006], 3006, 1, 1, now.AddMinutes(-8)),
            BuildAggregate(account1Id, 55, "16.5", "KR", 420, "MIDDLE", 4, 14, "Q-E-W", [3007], 3007, 1, 0, now.AddMinutes(-7)),
            BuildAggregate(account2Id, 55, "16.5", "KR", 420, "MIDDLE", 7, 4, "Q-W-E", [3006], 3006, 1, 1, now.AddMinutes(-6)),
            BuildAggregate(account2Id, 55, "16.5", "KR", 420, "MIDDLE", 14, 4, "Q-E-W", [3007], 3007, 1, 0, now.AddMinutes(-5)),
            BuildAggregate(account1Id, 55, "16.4", "KR", 420, "MIDDLE", 4, 7, "Q-W-E", [3020], 3020, 3, 2, now.AddDays(-1)),
            BuildAggregate(account1Id, 55, "16.8", "KR", 450, "MIDDLE", 4, 7, "Q-W-E", [3089], 3089, 2, 2, now.AddMinutes(-1)));

        await db.SaveChangesAsync();
    }

    private async Task SeedChampionFoundationCorrelationAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var account2Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var account3Id = Guid.Parse("12121212-3434-5656-7878-909090909090");

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "KR", "corr-puuid-1", "corr-one", now),
            BuildAccount(account2Id, "KR", "corr-puuid-2", "corr-two", now),
            BuildAccount(account3Id, "KR", "corr-puuid-3", "corr-three", now));

        db.ChampionPatternAggregates.AddRange(
            BuildAggregate(account1Id, 81, "16.5", "KR", 420, "BOTTOM", 4, 7, "Q-W-E", [3153, 3006, 3091], 3006, 3, 2, now.AddMinutes(-10)),
            BuildAggregate(account2Id, 81, "16.5", "KR", 420, "BOTTOM", 4, 7, "Q-W-E", [3153, 3006, 3091], 3006, 3, 2, now.AddMinutes(-9)),
            BuildAggregate(account3Id, 81, "16.5", "KR", 420, "BOTTOM", 14, 4, "Q-E-W", [6672, 3006, 3031], 3047, 5, 4, now.AddMinutes(-8)));

        await db.SaveChangesAsync();
    }

    private async Task SeedChampionFoundationWithEmptyTopPatternAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("abababab-abab-abab-abab-abababababab");
        var account2Id = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "KR", "empty-build-puuid-1", "empty-build-one", now),
            BuildAccount(account2Id, "KR", "empty-build-puuid-2", "empty-build-two", now));

        db.ChampionPatternAggregates.AddRange(
            BuildAggregate(account1Id, 110, "16.5", "KR", 420, "MIDDLE", 14, 4, "Q-E-W", [], 0, 6, 4, now.AddMinutes(-12)),
            BuildAggregate(account2Id, 110, "16.5", "KR", 420, "MIDDLE", 4, 7, "Q-W-E", [3153, 3006, 3091], 3006, 5, 3, now.AddMinutes(-10)));

        await db.SaveChangesAsync();
    }

    private static RiotAccount BuildAccount(Guid id, string platformId, string puuid, string gameName, DateTime now)
        => new()
        {
            Id = id,
            PlatformId = platformId,
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
        int summoner1Id,
        int summoner2Id,
        string skillOrderKey,
        IReadOnlyList<int> buildItems,
        int bootsItemId,
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
            SummonerSpell1Id = summoner1Id,
            SummonerSpell2Id = summoner2Id,
            SkillOrderKey = skillOrderKey,
            StarterItems = [1055, 2003],
            StarterItemsKey = "1055-2003",
            BootsItemId = bootsItemId,
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

    private static void AssertObjectArrayElementsHaveProperties(JsonElement arrayElement, params string[] expectedPropertyNames)
    {
        foreach (var element in arrayElement.EnumerateArray())
        {
            element.EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo(expectedPropertyNames);
        }
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
