using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

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
            ["sampleSize", "starterItems", "boots", "buildPath", "summonerSpells", "skillOrder", "runePage"]);

        var advancedProperties = root.GetProperty("advanced").EnumerateObject().Select(property => property.Name);
        advancedProperties.Should().BeEquivalentTo(
            ["starterItemOptions", "summonerSpellOptions", "skillOrderOptions", "runePageOptions"]);

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
    public async Task GetFoundationAsync_ShouldOnlySurfaceBuildPathsFromRowsThatHaveBuilds()
    {
        // Post Sprint 5.4: Core.SummonerSpells / .SkillOrder are picked
        // independently from their dimensions (top-by-games across scopes),
        // so they can differ from the top build path that Core.BuildPath
        // comes from. What the test still proves is that BuildPath /
        // BuildTree skip scopes whose build is empty — account1's empty
        // build cannot contaminate the build-tree output.
        await _fixture.ResetDatabaseAsync();
        await SeedChampionFoundationWithEmptyTopPatternAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionReadModel>("/champions/110");

        payload.Should().NotBeNull();
        payload!.Core.BuildPath.Should().NotBeNull();
        payload.Core.BuildPath!.ItemIds.Should().Equal(3153, 3006, 3091);
        // (14, 4) is observed 6 times on account1, (4, 7) 5 times on account2.
        // OrderedForDisplay puts Flash (4) first → (4, 14) is the top pair.
        payload.Core.SummonerSpells.Should().NotBeNull();
        payload.Core.SummonerSpells!.Spell1Id.Should().Be(4);
        payload.Core.SummonerSpells.Spell2Id.Should().Be(14);
        payload.Core.SkillOrder.Should().NotBeNull();
        payload.Core.SkillOrder!.Sequence.Should().Equal("Q", "E", "W");
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

    [Fact]
    public async Task GetFoundationAsync_WithBuildIdPivot_FiltersAdvancedDimensionsToThatBuild()
    {
        // Phase 6.3 — the cross-dim correlation pivot. Two builds, two
        // distinct skill orders: each skill order is associated with one
        // build via the pattern junction. Without a pivot the foundation
        // sees both skill orders. Pivoting on build A's id should leave
        // only the skill order paired with build A.
        await _fixture.ResetDatabaseAsync();
        var account = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var seedDb = _fixture.CreateDbContext();
        seedDb.RiotAccounts.Add(BuildAccount(account, "KR", "pivot-puuid", "pivot", DateTime.UtcNow));
        await seedDb.SaveChangesAsync();

        await DefaultSeeder()
            .AddPatternDefaults(account, 33, "16.4", "KR", 420, "MIDDLE", 4, 7, "Q-W-E", [6672, 3094, 3031], 3006, 5, 3, DateTime.UtcNow.AddMinutes(-10))
            .AddPatternDefaults(account, 33, "16.4", "KR", 420, "MIDDLE", 4, 7, "Q-E-W", [3153, 3006, 3091], 3006, 4, 2, DateTime.UtcNow.AddMinutes(-5))
            .SaveAsync(seedDb);

        // Discover the dim build id for build A (BuildItem0 = 6672) so the
        // test doesn't depend on the seeder's GUIDs.
        await using var lookupDb = _fixture.CreateDbContext();
        var buildA = await lookupDb.Set<Data.Entities.ChampionDimBuild>()
            .Where(build => build.BuildItem0 == 6672)
            .Select(build => build.Id)
            .FirstAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var unpivoted = await client.GetFromJsonAsync<ChampionReadModel>("/champions/33");
        unpivoted.Should().NotBeNull();
        unpivoted!.Advanced.SkillOrderOptions.Should().HaveCount(2);

        var pivoted = await client.GetFromJsonAsync<ChampionReadModel>($"/champions/33?buildId={buildA}");
        pivoted.Should().NotBeNull();
        pivoted!.Advanced.SkillOrderOptions.Should().ContainSingle();
        pivoted.Advanced.SkillOrderOptions[0].Sequence.Should().Equal("Q", "W", "E");
        pivoted.Core.SampleSize.Should().Be(5);
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
        await db.SaveChangesAsync();

        await DefaultSeeder()
            .AddPatternDefaults(account1Id, 22, "16.4", "KR", 420, "BOTTOM", 4, 7, "Q-W-E", [6672, 3006, 3094], 3006, 2, 1, now.AddMinutes(-10))
            .AddPatternDefaults(account2Id, 22, "16.4", "KR", 420, "BOTTOM", 7, 4, "Q-W-E", [6672, 3006, 3094], 3006, 1, 1, now.AddMinutes(-5))
            .AddPatternDefaults(account2Id, 22, "16.3", "KR", 420, "BOTTOM", 4, 7, "Q-E-W", [6672, 3085, 3031], 3111, 5, 2, now.AddDays(-2))
            .AddPatternDefaults(account1Id, 22, "16.9", "KR", 450, "BOTTOM", 4, 7, "Q-W-E", [6672, 3094, 3031], 3006, 3, 2, now.AddMinutes(-1))
            .SaveAsync(db);
    }

    private static ChampionAggregateSeeder DefaultSeeder() => new();

    private async Task SeedChampionFoundationTieAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var account1Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var account2Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(account1Id, "KR", "tie-foundation-puuid-1", "tie-one", now),
            BuildAccount(account2Id, "KR", "tie-foundation-puuid-2", "tie-two", now));
        await db.SaveChangesAsync();

        await DefaultSeeder()
            .AddPatternDefaults(account1Id, 55, "16.5", "KR", 420, "MIDDLE", 4, 7, "Q-W-E", [3006], 3006, 1, 1, now.AddMinutes(-8))
            .AddPatternDefaults(account1Id, 55, "16.5", "KR", 420, "MIDDLE", 4, 14, "Q-E-W", [3007], 3007, 1, 0, now.AddMinutes(-7))
            .AddPatternDefaults(account2Id, 55, "16.5", "KR", 420, "MIDDLE", 7, 4, "Q-W-E", [3006], 3006, 1, 1, now.AddMinutes(-6))
            .AddPatternDefaults(account2Id, 55, "16.5", "KR", 420, "MIDDLE", 14, 4, "Q-E-W", [3007], 3007, 1, 0, now.AddMinutes(-5))
            .AddPatternDefaults(account1Id, 55, "16.4", "KR", 420, "MIDDLE", 4, 7, "Q-W-E", [3020], 3020, 3, 2, now.AddDays(-1))
            .AddPatternDefaults(account1Id, 55, "16.8", "KR", 450, "MIDDLE", 4, 7, "Q-W-E", [3089], 3089, 2, 2, now.AddMinutes(-1))
            .SaveAsync(db);
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
        await db.SaveChangesAsync();

        await DefaultSeeder()
            .AddPatternDefaults(account1Id, 81, "16.5", "KR", 420, "BOTTOM", 4, 7, "Q-W-E", [3153, 3006, 3091], 3006, 3, 2, now.AddMinutes(-10))
            .AddPatternDefaults(account2Id, 81, "16.5", "KR", 420, "BOTTOM", 4, 7, "Q-W-E", [3153, 3006, 3091], 3006, 3, 2, now.AddMinutes(-9))
            .AddPatternDefaults(account3Id, 81, "16.5", "KR", 420, "BOTTOM", 14, 4, "Q-E-W", [6672, 3006, 3031], 3047, 5, 4, now.AddMinutes(-8))
            .SaveAsync(db);
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
        await db.SaveChangesAsync();

        await DefaultSeeder()
            .AddPatternDefaults(account1Id, 110, "16.5", "KR", 420, "MIDDLE", 14, 4, "Q-E-W", [], 0, 6, 4, now.AddMinutes(-12))
            .AddPatternDefaults(account2Id, 110, "16.5", "KR", 420, "MIDDLE", 4, 7, "Q-W-E", [3153, 3006, 3091], 3006, 5, 3, now.AddMinutes(-10))
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
                    new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                    new KeyValuePair<string, string?>("Ops:ApiKey", "integration-tests-ops-key-0123456789-padding")
                ]);
            });
        }
    }
}
