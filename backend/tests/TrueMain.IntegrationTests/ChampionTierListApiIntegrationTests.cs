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

        // Every emitted tier group carries at least one row (empty tiers are
        // omitted upstream). Within-tier strongest-first ordering is by the
        // blended winRate + pickRate score, which is NOT monotonic in winRate
        // alone once a group merges rows from several positions (each scored
        // against its own per-position max pickRate) — so that ordering is
        // asserted on the single-position path below and in the unit tests,
        // not here.
        tierList.Tiers.Should().OnlyContain(group => group.Entries.Count > 0);
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
    public async Task GetTierList_TierMatchesTheDirectoryForEveryRow()
    {
        // #971 / #1240: the tier is computed once, by ChampionSummariesQueryService,
        // and GET /champions/tierlist only regroups the rows it already stamped.
        // A row's Tier must therefore be identical on both endpoints. The blended
        // score is not serialized on ChampionTierEntryReadModel, so it is checked
        // indirectly below: the tier list's within-group order has to be the
        // directory rows sorted by TierScore desc, then ChampionId.
        await _fixture.ResetDatabaseAsync();
        await SeedManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var directoryResponse = await client.GetAsync("/champions");
        directoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var directory = await directoryResponse.Content.ReadFromJsonAsync<IReadOnlyList<ChampionSummaryReadModel>>();
        directory.Should().NotBeNull();

        var tierListResponse = await client.GetAsync("/champions/tierlist");
        tierListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tierList = await tierListResponse.Content.ReadFromJsonAsync<ChampionTierListReadModel>();
        tierList.Should().NotBeNull();

        // The tier letter lives on the group, not the entry — flatten each
        // group's tier onto its entries to get a per-row lookup.
        var tierByEntry = tierList!.Tiers
            .SelectMany(group => group.Entries.Select(entry => (Key: (entry.ChampionId, entry.Position), group.Tier)))
            .ToDictionary(pair => pair.Key, pair => pair.Tier);
        tierByEntry.Should().HaveCount(60, "the same 60 seeded rows are tiered by both endpoints");

        foreach (var row in directory!)
        {
            tierByEntry[(row.ChampionId, row.Position)].Should().Be(row.Tier,
                $"champion {row.ChampionId}/{row.Position} must tier the same on both endpoints");
        }

        // Ordering inside a group is the directory's TierScore, strongest first,
        // ChampionId breaking exact ties. Reproducing it from the directory rows
        // is what pins the score itself, which the entry model doesn't expose.
        var scoreByRow = directory.ToDictionary(row => (row.ChampionId, row.Position));
        foreach (var group in tierList.Tiers)
        {
            var expected = group.Entries
                .Select(entry => scoreByRow[(entry.ChampionId, entry.Position)])
                .OrderByDescending(row => row.TierScore)
                .ThenBy(row => row.ChampionId)
                .Select(row => (row.ChampionId, row.Position))
                .ToList();

            group.Entries.Select(entry => (entry.ChampionId, entry.Position)).Should().Equal(expected,
                $"tier {group.Tier} is ordered by the directory's own TierScore");
        }
    }

    [Fact]
    public async Task GetTierList_KeepsTheSameTierWhenScopedToOnePosition()
    {
        // The position filter drops whole lanes, never rows inside a kept lane,
        // and the tier is lane-relative — so scoping to MIDDLE cannot move a
        // MIDDLE row to another tier. This is the invariant that let #1240
        // delete the tier list's own re-tiering pass.
        await _fixture.ResetDatabaseAsync();
        await SeedManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var allResponse = await client.GetAsync("/champions/tierlist");
        allResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var all = await allResponse.Content.ReadFromJsonAsync<ChampionTierListReadModel>();
        all.Should().NotBeNull();

        var middleResponse = await client.GetAsync("/champions/tierlist?position=MIDDLE");
        middleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var middle = await middleResponse.Content.ReadFromJsonAsync<ChampionTierListReadModel>();
        middle.Should().NotBeNull();

        var unscopedMiddleTiers = all!.Tiers
            .SelectMany(group => group.Entries
                .Where(entry => entry.Position == "MIDDLE")
                .Select(entry => (entry.ChampionId, group.Tier)))
            .OrderBy(pair => pair.ChampionId)
            .ToList();

        var scopedMiddleTiers = middle!.Tiers
            .SelectMany(group => group.Entries.Select(entry => (entry.ChampionId, group.Tier)))
            .OrderBy(pair => pair.ChampionId)
            .ToList();

        scopedMiddleTiers.Should().Equal(unscopedMiddleTiers,
            "a lane's tiers are the same whether or not the list is scoped to it");
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
