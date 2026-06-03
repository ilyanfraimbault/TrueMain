using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionTrendApiIntegrationTests
{
    private const int ChampionId = 157; // Yone
    private const int OtherChampionId = 238; // Zed — fattens the MIDDLE lane total

    // Three consecutive patches on MIDDLE for the champion under test, seeded
    // out of release order to prove the endpoint sorts by patch, not by row
    // order. Tuple = (patch, games, wins).
    private static readonly (string Patch, int Games, int Wins)[] MiddleSeries =
    [
        ("16.3", 40, 18),
        ("16.5", 60, 36),
        ("16.4", 50, 30),
    ];

    private readonly PostgresFixture _fixture;

    public ChampionTrendApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionTrendAsync_ReturnsPerPatchSeriesOldestToNewest()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTrendAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // No position → the endpoint defaults to the champion's dominant lane
        // on its latest patch (MIDDLE here; the BOTTOM noise row is smaller).
        var response = await client.GetAsync($"/champions/{ChampionId}/trend");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var trend = await response.Content.ReadFromJsonAsync<ChampionTrendReadModel>();
        trend.Should().NotBeNull();
        trend!.ChampionId.Should().Be(ChampionId);
        trend.Position.Should().Be("MIDDLE", "the dominant MIDDLE lane on the latest patch is the default");

        trend.Points.Select(point => point.Patch).Should().Equal(
            ["16.3", "16.4", "16.5"],
            "the series is ordered oldest → newest by patch regardless of seed order");

        // 16.4: Yone 50 wins-rate 30/50, lane total = Yone 50 + Zed 30 = 80.
        var mid = trend.Points.Single(point => point.Patch == "16.4");
        mid.Games.Should().Be(50);
        mid.WinRate.Should().BeApproximately(30d / 50d, 1e-9, "Wins / Games for the slice");
        mid.PickRate.Should().BeApproximately(50d / 80d, 1e-9,
            "PickRate = slice games / total MIDDLE games on the patch (Yone 50 + Zed 30)");
    }

    [Fact]
    public async Task GetChampionTrendAsync_RespectsRequestedPosition()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTrendAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // BOTTOM has a single seeded patch for this champion; the filter must
        // pin the series to it instead of the dominant MIDDLE lane.
        var response = await client.GetAsync($"/champions/{ChampionId}/trend?position=bottom");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var trend = await response.Content.ReadFromJsonAsync<ChampionTrendReadModel>();
        trend.Should().NotBeNull();
        trend!.Position.Should().Be("BOTTOM", "the canonicalised ?position filter is honoured");
        trend.Points.Should().ContainSingle().Which.Patch.Should().Be("16.5");
    }

    [Fact]
    public async Task GetChampionTrendAsync_ReturnsEmptySeriesForUnknownChampion()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTrendAggregatesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // A champion the directory never observed gets a 200 with no points,
        // so the chart renders its own empty state rather than a 404.
        var response = await client.GetAsync("/champions/99999/trend");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var trend = await response.Content.ReadFromJsonAsync<ChampionTrendReadModel>();
        trend.Should().NotBeNull();
        trend!.Points.Should().BeEmpty();
    }

    private async Task SeedTrendAggregatesAsync()
    {
        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = accountId,
            PlatformId = "KR",
            Puuid = "trend-puuid-1",
            GameName = "trend-one",
            SummonerId = "trend-one-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var seeder = new ChampionAggregateSeeder();

        // Champion under test: three MIDDLE patches + a single smaller BOTTOM
        // patch (so MIDDLE stays the dominant lane on the latest patch).
        foreach (var (patch, games, wins) in MiddleSeries)
        {
            seeder.AddPatternWithRune(
                accountId, ChampionId, patch, "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: games, wins: wins, aggregatedAtUtc: now);
        }

        seeder.AddPatternWithRune(
            accountId, ChampionId, "16.5", "KR", 420, "BOTTOM",
            summoner1Id: 4, summoner2Id: 7, skillOrderKey: "Q-W-E",
            buildItems: [3153, 3006, 3031], bootsItemId: 3006,
            primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
            games: 10, wins: 5, aggregatedAtUtc: now);

        // Second champion on MIDDLE so the pickrate denominator (lane total)
        // is larger than this champion's own games on each patch.
        var zedMiddle = new[] { ("16.3", 20), ("16.4", 30), ("16.5", 40) };
        foreach (var (patch, games) in zedMiddle)
        {
            seeder.AddPatternWithRune(
                accountId, OtherChampionId, patch, "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3142, 3006, 3814], bootsItemId: 3006,
                primaryStyleId: 8100, primaryKeystoneId: 8112, secondaryStyleId: 8000,
                games: games, wins: games / 2, aggregatedAtUtc: now);
        }

        await seeder.SaveAsync(db);
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
}
