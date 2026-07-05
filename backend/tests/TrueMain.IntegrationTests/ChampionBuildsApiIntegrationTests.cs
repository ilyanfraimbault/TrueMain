using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Lol.Ranking;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionBuildsApiIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ChampionBuildsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionAsync_ReturnsTabbedBuildsForTheLatestPatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionBuildsAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/157");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Top-level contract
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["championId", "patch", "position", "eloBracket", "eloCoverage", "minSampleMet",
             "totalGames", "totalWins", "builds"]);

        // Per-build contract — covers the four UI sections + tab key
        AssertObjectArrayElementsHaveProperties(root.GetProperty("builds"),
            "firstItemId", "primaryKeystoneId", "games", "pickRate", "winRate",
            "core", "variations", "buildTree", "runePages");

        var payload = await response.Content.ReadFromJsonAsync<ChampionResponse>();
        payload.Should().NotBeNull();
        payload!.ChampionId.Should().Be(157);
        payload.Patch.Should().Be("16.5");
        payload.Position.Should().Be("MIDDLE");
        payload.TotalGames.Should().Be(11);
        payload.TotalWins.Should().Be(6);

        // 3153+Lethal Tempo: 6 games (2+4), pickRate 6/11 ≈ 0.545
        // 3153+Conqueror:    3 games,        pickRate 3/11 ≈ 0.273
        // 6673+Conqueror:    2 games,        pickRate 2/11 ≈ 0.182
        payload.Builds.Should().HaveCount(3, "above the 5% pickRate floor");
        payload.Builds.Select(b => (b.FirstItemId, b.PrimaryKeystoneId))
            .Should().Equal(
                (3153, 8008),
                (3153, 8010),
                (6673, 8010));

        var top = payload.Builds[0];
        top.Games.Should().Be(6);
        top.PickRate.Should().BeApproximately(6d / 11d, 1e-9);
        top.WinRate.Should().BeApproximately(3d / 6d, 1e-9);

        // Core: dominant single for each dimension within the slice
        top.Core.RunePage.Should().NotBeNull();
        top.Core.RunePage!.PrimaryKeystoneId.Should().Be(8008);
        top.Core.RunePage.SecondaryStyleId.Should().Be(8400);
        top.Core.Boots.Should().NotBeNull();
        top.Core.Boots!.ItemIds.Should().Equal(3006);
        top.Core.SummonerSpells.Should().NotBeNull();
        top.Core.SummonerSpells!.Spell1Id.Should().Be(4);
        top.Core.SummonerSpells.Spell2Id.Should().Be(12);

        // Variations: top-3 lists including the dominant
        top.Variations.Boots.Should().NotBeEmpty();
        top.Variations.Boots[0].ItemIds.Should().Equal(3006);

        // BuildTree: rooted at firstItemId, children carry conditional pickRate
        top.BuildTree.Should().NotBeEmpty();
        top.BuildTree[0].ItemId.Should().Be(3006);
        top.BuildTree[0].PickRate.Should().BeApproximately(1d, 1e-9); // 6/6 reached the build

        // RunePages: top-N entries, all on the tab's keystone
        top.RunePages.Should().NotBeEmpty();
        top.RunePages.Should().OnlyContain(page => page.PrimaryKeystoneId == 8008);
    }

    [Fact]
    public async Task GetChampionAsync_ReturnsNotFoundForUnknownChampion()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/9999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetChampionAsync_DefaultBracket_UnionsEveryBand()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMultiBracketAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // No eloBracket query → the synthetic ALL union across every band.
        var payload = await client.GetFromJsonAsync<ChampionResponse>("/champions/777");
        payload.Should().NotBeNull();
        payload!.EloBracket.Should().Be(EloBracket.All);
        payload.Patch.Should().Be("16.5");
        payload.Position.Should().Be("MIDDLE");

        // 80 (Iron–Gold) + 10 (Master+) games across the two seeded bands.
        payload.TotalGames.Should().Be(90);
        payload.EloCoverage.Should().Be(1d, "the ALL union covers every game by definition");
        payload.MinSampleMet.Should().BeTrue();

        // Both bands' distinct (firstItem, keystone) tabs surface.
        payload.Builds.Select(b => (b.FirstItemId, b.PrimaryKeystoneId))
            .Should().BeEquivalentTo([(3153, 8008), (6673, 8010)]);
    }

    [Fact]
    public async Task GetChampionAsync_NarrowBracket_ScopesToThatBandAndReportsCoverage()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMultiBracketAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionResponse>(
            "/champions/777?eloBracket=MASTER_PLUS");
        payload.Should().NotBeNull();
        payload!.EloBracket.Should().Be(EloBracket.MasterPlus);

        // Only the Master+ band's 10 games and its single tab remain.
        payload.TotalGames.Should().Be(10);
        payload.Builds.Select(b => (b.FirstItemId, b.PrimaryKeystoneId))
            .Should().Equal((6673, 8010));

        // 10 / (80 + 10) all-rank games, and below the 20-game sample floor.
        payload.EloCoverage.Should().BeApproximately(10d / 90d, 1e-9);
        payload.MinSampleMet.Should().BeFalse("10 games is under the min-sample floor");
    }

    [Fact]
    public async Task GetChampionAsync_UnrankedBracket_IsNotSelectableAndYieldsNoData()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMultiBracketAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // No seeded games fall in an UNRANKED scope, so the slice is empty → 404.
        var response = await client.GetAsync("/champions/777?eloBracket=UNRANKED");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task SeedMultiBracketAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
        var account2Id = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "KR", "bracket-puuid-1", "bracket-one", now),
            BuildAccount(account2Id, "KR", "bracket-puuid-2", "bracket-two", now));
        await db.SaveChangesAsync();

        // Champion 777, patch 16.5, MIDDLE, queue 420. Two elo bands, each with a
        // distinct (firstItem, keystone) tab so we can tell them apart:
        // - Iron–Gold : 3153 + 8008, 80 games (the dominant band).
        // - Master+   : 6673 + 8010, 10 games (a thin high-elo slice).
        await new ChampionAggregateSeeder()
            .AddPatternWithRune(account1Id, 777, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 80, wins: 42, aggregatedAtUtc: now.AddMinutes(-10),
                eloBracket: EloBracket.IronGold)
            .AddPatternWithRune(account2Id, 777, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [6673, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8010, secondaryStyleId: 8400,
                games: 10, wins: 7, aggregatedAtUtc: now.AddMinutes(-9),
                eloBracket: EloBracket.MasterPlus)
            .SaveAsync(db);
    }

    private async Task SeedChampionBuildsAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var account2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "KR", "builds-puuid-1", "builds-one", now),
            BuildAccount(account2Id, "KR", "builds-puuid-2", "builds-two", now));
        await db.SaveChangesAsync();

        // Champion 157 (Yone), patch 16.5, MIDDLE, queue 420 (matches MainAnalysisOptions test override)
        //
        // Three distinct (firstItem, keystone) slices over 11 games:
        // - 3153 (Botrk) + 8008 (Lethal Tempo): 6 games, 3 wins → dominant tab.
        //   Two pattern rows share the same first item + keystone so the
        //   BuildTree groups the 3006 child under one parent.
        // - 3153 (Botrk) + 8010 (Conqueror):    3 games, 2 wins → second tab.
        // - 6673 (Eclipse) + 8010 (Conqueror):  2 games, 1 win → third tab.
        await new ChampionAggregateSeeder()
            .AddPatternWithRune(account1Id, 157, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 4, wins: 2, aggregatedAtUtc: now.AddMinutes(-10))
            .AddPatternWithRune(account2Id, 157, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                buildItems: [3153, 3006, 3094], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 2, wins: 1, aggregatedAtUtc: now.AddMinutes(-9))
            .AddPatternWithRune(account1Id, 157, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3047, 3031], bootsItemId: 3047,
                primaryStyleId: 8000, primaryKeystoneId: 8010, secondaryStyleId: 8300,
                games: 3, wins: 2, aggregatedAtUtc: now.AddMinutes(-8))
            .AddPatternWithRune(account2Id, 157, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [6673, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8010, secondaryStyleId: 8400,
                games: 2, wins: 1, aggregatedAtUtc: now.AddMinutes(-7))
            // Older patch — must be filtered out (the latest is 16.5).
            .AddPatternWithRune(account1Id, 157, "16.4", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 10, wins: 4, aggregatedAtUtc: now.AddDays(-2))
            .SaveAsync(db);
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

    private static void AssertObjectArrayElementsHaveProperties(JsonElement arrayElement, params string[] expectedPropertyNames)
    {
        foreach (var element in arrayElement.EnumerateArray())
        {
            element.EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo(expectedPropertyNames);
        }
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
}
