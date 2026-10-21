using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The read surface of the next-item model (#1749): the terms the fold derived, combined at
/// read time with the situation the posted game sits in.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionNextItemApiIntegrationTests : IAsyncLifetime
{
    private const int Champion = 266;
    private const string Position = "TOP";
    private const string Patch = "16.19";
    private const string OlderPatch = "16.9";

    private const int Armor = 3143;
    private const int MagicResist = 4401;
    private const int Health = 3083;
    private const int Ninja = 3047;
    private const int Mercs = 3111;
    private const int Warden = 3082;
    private const int Belt = 1011;
    private const int Boots = 1001;

    /// <summary>Five enemies whose profiles deal almost only magic damage.</summary>
    private static readonly int[] MagicEnemies = [1, 7, 8, 10, 13];

    private static readonly string[] Lanes = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    private readonly PostgresFixture _fixture;

    public ChampionNextItemApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task NextItem_ServesTheBaseOrder_ForAGameItKnowsNothingAbout()
    {
        await SeedAsync();

        var response = await PostAsync(new { position = Position, items = Array.Empty<int>() });

        response.Patch.Should().Be(Patch, "16.19 is newer than 16.9 even though it sorts below it as a string");
        response.Build!.ParentItemId.Should().Be(0);
        response.Build.OnTree.Should().BeTrue();
        response.Build.Candidates.Select(candidate => candidate.ItemId).Should().Equal(Armor, MagicResist);
        response.Build.Candidates[0].Share.Should().BeApproximately(0.6, 1e-9);
        response.Boots!.Candidates.Select(candidate => candidate.ItemId).Should().Equal(Ninja, Mercs);
    }

    [Fact]
    public async Task NextItem_ReordersTheCandidates_AgainstAMagicDamageTeam()
    {
        await SeedAsync();

        var response = await PostAsync(new
        {
            position = Position,
            items = Array.Empty<int>(),
            enemies = MagicEnemies.Select((id, i) => new { championId = id, position = Lanes[i] }),
        });

        response.Situation.Should().ContainKey("EnemyMagicDamage").WhoseValue.Should().Be("High");
        var first = response.Build!.Candidates[0];
        first.ItemId.Should().Be(MagicResist);
        first.BaseShare.Should().BeApproximately(0.4, 1e-9);
        first.Reasons.Should().ContainSingle().Which.Axis.Should().Be("EnemyMagicDamage");
        first.Reasons[0].Factor.Should().BeGreaterThan(1);
        response.EnemyLanes.Should().OnlyContain(lane => lane.Given);
    }

    [Fact]
    public async Task NextItem_WalksTheBuildAndDropsTheBootsOnceHeld()
    {
        await SeedAsync();

        var response = await PostAsync(new { position = Position, items = new[] { 1055, Armor, Ninja } });

        response.Build!.ParentItemId.Should().Be(Armor);
        response.Build.Candidates.Should().ContainSingle().Which.ItemId.Should().Be(Health);
        response.Boots.Should().BeNull("the game already holds a pair the mains settle on");
    }

    [Fact]
    public async Task NextItem_LaysTheBuildOutAsOneRow_WithTheBootsAtTheirStep()
    {
        await SeedAsync();

        var fresh = await PostAsync(new { position = Position, items = Array.Empty<int>() });
        fresh.Order.Select(step => (step.ItemId, step.State)).Should().Equal(
            (Armor, NextItemOrderState.Next),
            (Ninja, NextItemOrderState.Upcoming),
            (Health, NextItemOrderState.Upcoming));

        var afterFirst = await PostAsync(new { position = Position, items = new[] { 1055, Armor } });
        afterFirst.Order.Select(step => (step.ItemId, step.State)).Should().Equal(
            (Armor, NextItemOrderState.Completed),
            (Ninja, NextItemOrderState.Next),
            (Health, NextItemOrderState.Upcoming));
        afterFirst.Order[1].IsBoots.Should().BeTrue();
    }

    [Fact]
    public async Task NextItem_CarriesEachCandidatesBuildPath_InTheOrderTheMainsBuyIt()
    {
        await SeedAsync();
        await using (var db = _fixture.CreateDbContext())
        {
            // The recipe lists the Warden's Mail first; the mains start on the Belt in 24 of 30 games.
            db.ChampionItemComponentOrders.AddRange(
                Order(Armor, Belt, 1, 24), Order(Armor, Belt, 2, 6),
                Order(Armor, Warden, 1, 6), Order(Armor, Warden, 2, 24));
            await db.SaveChangesAsync();
        }

        var response = await PostAsync(new { position = Position, items = Array.Empty<int>() });

        var armor = response.Build!.Candidates.Single(candidate => candidate.ItemId == Armor);
        armor.Route.Select(step => (step.ItemId, step.Rank, step.Cost)).Should().Equal((Belt, 1, 900), (Warden, 2, 1000));
        armor.Route[0].Share.Should().BeApproximately(0.8, 1e-9);
        armor.Route.Should().OnlyContain(step => step.Measured);
        armor.RouteGames.Should().Be(30);

        // No counts for the boots: their recipe stands, and says it is not measured.
        var ninja = response.Boots!.Candidates.Single(candidate => candidate.ItemId == Ninja);
        ninja.Route.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ItemRouteStepReadModel { ItemId = Boots, Cost = 300, Rank = 1, Share = null, Measured = false });
    }

    [Fact]
    public async Task NextItem_PlacesUnlanedEnemiesAroundTheGivenOnes()
    {
        await SeedAsync();

        var response = await PostAsync(new
        {
            position = Position,
            enemies = new object[] { new { championId = MagicEnemies[0], position = "TOP" }, new { championId = MagicEnemies[1] } },
        });

        response.EnemyLanes.Should().HaveCount(2);
        response.EnemyLanes[0].Should().BeEquivalentTo(new NextItemEnemyLaneReadModel { ChampionId = MagicEnemies[0], Position = "TOP", Given = true });
        response.EnemyLanes[1].Given.Should().BeFalse();
        response.EnemyLanes[1].Position.Should().NotBe("TOP");
    }

    [Fact]
    public async Task NextItem_AnswersEmpty_ForAChampionWithNoModel()
    {
        var response = await PostAsync(new { position = Position });

        response.Patch.Should().BeNull();
        response.Build.Should().BeNull();
        response.Boots.Should().BeNull();
    }

    [Fact]
    public async Task NextItem_RequiresALane()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync($"/champions/{Champion}/next-item", new { position = "MID LANE" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<ChampionNextItemResponse> PostAsync(object body)
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);
        var response = await client.PostAsJsonAsync($"/champions/{Champion}/next-item", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ChampionNextItemResponse>())!;
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    /// <summary>
    /// API factory with a deterministic item-metadata source, so the build paths never call
    /// CommunityDragon from the test suite.
    /// </summary>
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")])
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IItemMetadataProvider>();
                services.AddSingleton<IItemMetadataProvider>(new RecipeMetadataProvider());
            });
        }
    }

    private sealed class RecipeMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Items = new Dictionary<int, ItemMetadata>
        {
            // Id, PriceTotal, InStore, IsConsumable, IsBootsItem, IsBaseBoots, IsFinalItem, IsFinalBoots
            [Armor] = new(Armor, 2700, true, false, false, false, true, false) { Components = [Warden, Belt] },
            [Warden] = new(Warden, 1000, true, false, false, false, false, false),
            [Belt] = new(Belt, 900, true, false, false, false, false, false),
            [Ninja] = new(Ninja, 1200, true, false, true, false, true, true) { Components = [Boots] },
            [Boots] = new(Boots, 300, true, false, true, true, false, false),
        };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Items);
    }

    private static ChampionItemComponentOrder Order(int item, int component, int rank, int games)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = Patch,
            ItemId = item,
            ComponentId = component,
            Rank = rank,
            Games = games,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private async Task SeedAsync()
    {
        await using var db = _fixture.CreateDbContext();

        // Root: armour 60%, MR 40%; in magic-heavy games MR doubles. After armour: health.
        db.ChampionNextItemTerms.AddRange(
            Term(Patch, ItemContextSlot.Build, 0, Armor, ItemContextAxis.Overall, ItemContextBucket.All, Math.Log(0.6)),
            Term(Patch, ItemContextSlot.Build, 0, MagicResist, ItemContextAxis.Overall, ItemContextBucket.All, Math.Log(0.4)),
            Term(Patch, ItemContextSlot.Build, 0, MagicResist, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, Math.Log(2)),
            Term(Patch, ItemContextSlot.Build, Armor, Health, ItemContextAxis.Overall, ItemContextBucket.All, 0),
            Term(Patch, ItemContextSlot.Boots, 0, Ninja, ItemContextAxis.Overall, ItemContextBucket.All, Math.Log(0.7)),
            Term(Patch, ItemContextSlot.Boots, 0, Mercs, ItemContextAxis.Overall, ItemContextBucket.All, Math.Log(0.3)),
            // The boots come after the first legendary in 80% of the games, before it in 20%.
            Term(Patch, ItemContextSlot.BootsTiming, 0, 2, ItemContextAxis.Overall, ItemContextBucket.All, Math.Log(0.8)),
            Term(Patch, ItemContextSlot.BootsTiming, 0, 1, ItemContextAxis.Overall, ItemContextBucket.All, Math.Log(0.2)),
            // An older patch that a string sort would serve first.
            Term(OlderPatch, ItemContextSlot.Build, 0, Health, ItemContextAxis.Overall, ItemContextBucket.All, 0));

        foreach (var championId in MagicEnemies)
        {
            db.ChampionProfileStats.Add(new ChampionProfileStat
            {
                ChampionId = championId,
                Position = "MIDDLE",
                Patch = Patch,
                Games = 500,
                Wins = 250,
                GameDurationSecondsSum = 500 * 1800,
                MagicDamageToChampionsSum = 500 * 18_000,
                PhysicalDamageToChampionsSum = 500 * 1_000,
                TrueDamageToChampionsSum = 500 * 1_000,
                ItemGames = 500,
                IsRanged = true,
                AggregatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static ChampionNextItemTerm Term(
        string patch,
        ItemContextSlot slot,
        int parent,
        int item,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        double weight)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = patch,
            Slot = slot,
            ParentItemId = parent,
            ItemId = item,
            Axis = axis,
            Bucket = bucket,
            Weight = weight,
            Games = 100,
            Wins = 50,
            BranchGames = 1000,
            AggregatedAtUtc = DateTime.UtcNow,
        };
}
