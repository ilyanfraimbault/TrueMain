using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionTierListApiIntegrationTests
{
    private static readonly string[] ValidTiers = ["S", "A", "B", "C", "D"];

    private readonly PostgresFixture _fixture;

    public ChampionTierListApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetTierList_GroupsEveryRowIntoOrderedTiersForActivePatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/tierlist");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tierList = await response.Content.ReadFromJsonAsync<ChampionTierListReadModel>();
        tierList.Should().NotBeNull();
        tierList!.PatchVersion.Should().Be("16.5", "the seed wrote the active patch the tier list resolves to");
        tierList.Position.Should().BeNull("no position filter was supplied");

        // Every seeded (champion, position) row must land in exactly one tier
        // group, so the flattened entry count equals the 60 seeded slices and
        // every group carries a valid letter.
        tierList.Tiers.Should().NotBeEmpty();
        tierList.Tiers.Select(group => group.Tier).Should().OnlyContain(tier => ValidTiers.Contains(tier));

        var allEntries = tierList.Tiers.SelectMany(group => group.Entries).ToList();
        allEntries.Should().HaveCount(60, "all 60 seeded (champion, position) pairs are tiered");
        allEntries.Select(entry => (entry.ChampionId, entry.Position)).Should().OnlyHaveUniqueItems();

        // Tier groups come back in descending strength order (S before A ...).
        var emittedOrder = tierList.Tiers.Select(group => group.Tier).ToList();
        var sortedOrder = emittedOrder
            .OrderBy(tier => Array.IndexOf(ValidTiers, tier))
            .ToList();
        emittedOrder.Should().Equal(sortedOrder, "tier groups are emitted strongest-first");

        // The seed's games (and thus winRate / pickRate) climb with i, so a
        // 60-row field must span at least the top and bottom tiers.
        emittedOrder.Should().Contain("S").And.Contain("D",
            "a populated patch spans the full tier pyramid");

        // Within a tier, entries are ordered strongest-first: the blended score
        // is monotonic in winRate at constant-ish pickRate, so within any group
        // the leading entry's winRate is >= the trailing entry's.
        foreach (var group in tierList.Tiers)
        {
            group.Entries.Should().NotBeEmpty();
            group.Entries.First().WinRate.Should().BeGreaterThanOrEqualTo(group.Entries.Last().WinRate);
        }
    }

    [Fact]
    public async Task GetTierList_FiltersToASinglePosition()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/tierlist?position=MIDDLE");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tierList = await response.Content.ReadFromJsonAsync<ChampionTierListReadModel>();
        tierList.Should().NotBeNull();
        tierList!.Position.Should().Be("MIDDLE");

        var entries = tierList.Tiers.SelectMany(group => group.Entries).ToList();
        // The seeder writes positions[i % 5]; MIDDLE is i % 5 == 2, i.e. 12 of 60.
        entries.Should().HaveCount(12, "one in five seeded rows is a MIDDLE slice");
        entries.Should().OnlyContain(entry => entry.Position == "MIDDLE",
            "the position filter scopes the list to the requested lane");
    }

    [Fact]
    public async Task GetTierList_RejectsAnUnknownPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/tierlist?position=not-a-lane");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private ApiWebApplicationFactory CreateFactory() => new(_fixture, minSampleGames: 0);

    private async Task SeedManyChampionsAsync()
    {
        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = accountId,
            PlatformId = "KR",
            Puuid = "tierlist-puuid-1",
            GameName = "tierlist-one",
            SummonerId = "tierlist-one-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var seeder = new ChampionAggregateSeeder();
        var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        for (var i = 0; i < 60; i++)
        {
            var championId = 100 + i;
            var position = positions[i % positions.Length];
            seeder.AddPatternWithRune(
                accountId, championId, "16.5", "KR", 420, position,
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 10 + i, wins: 5 + (i % 4),
                aggregatedAtUtc: now.AddMinutes(-i));
        }

        await seeder.SaveAsync(db);
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, int minSampleGames)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinSampleGames", minSampleGames.ToString()),
            ]);
}
