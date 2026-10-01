using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.ItemContext;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The read surface of the next-item model (#1749): the terms the fold derived, combined at
/// read time with the situation the posted game sits in.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionNextItemApiIntegrationTests
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

    /// <summary>Five enemies whose profiles deal almost only magic damage.</summary>
    private static readonly int[] MagicEnemies = [1, 7, 8, 10, 13];

    private static readonly string[] Lanes = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    private readonly PostgresFixture _fixture;

    public ChampionNextItemApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task NextItem_ServesTheBaseOrder_ForAGameItKnowsNothingAbout()
    {
        await _fixture.ResetDatabaseAsync();
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
        await _fixture.ResetDatabaseAsync();
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
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        var response = await PostAsync(new { position = Position, items = new[] { 1055, Armor, Ninja } });

        response.Build!.ParentItemId.Should().Be(Armor);
        response.Build.Candidates.Should().ContainSingle().Which.ItemId.Should().Be(Health);
        response.Boots.Should().BeNull("the game already holds a pair the mains settle on");
    }

    [Fact]
    public async Task NextItem_PlacesUnlanedEnemiesAroundTheGivenOnes()
    {
        await _fixture.ResetDatabaseAsync();
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
        await _fixture.ResetDatabaseAsync();

        var response = await PostAsync(new { position = Position });

        response.Patch.Should().BeNull();
        response.Build.Should().BeNull();
        response.Boots.Should().BeNull();
    }

    [Fact]
    public async Task NextItem_RequiresALane()
    {
        await _fixture.ResetDatabaseAsync();

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

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);

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
