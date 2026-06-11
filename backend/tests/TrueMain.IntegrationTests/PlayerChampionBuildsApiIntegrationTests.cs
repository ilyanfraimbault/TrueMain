using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the player-scoped champion page
/// (<c>GET /truemains/{nameTag}/champions/{championId}</c>). The same two
/// accounts main champion 157 with intentionally different builds; the global
/// endpoint sums them while the player-scoped endpoint must reflect only the
/// requested player's games — proving the scope actually narrows.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PlayerChampionBuildsApiIntegrationTests
{
    private static readonly Guid AccountOneId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AccountTwoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly PostgresFixture _fixture;

    public PlayerChampionBuildsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetPlayerChampion_scopes_aggregates_to_the_requested_player()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Account one (Phantasm-EUW1): 8 games, all on first item 3153 + Lethal
        // Tempo (8008). Account two (Rival-EUW1): 6 games, all on first item
        // 6673 + Conqueror (8010). Globally these are two separate tabs; scoped
        // to Phantasm we should see ONLY the 3153/8008 build.
        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/157");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ChampionResponse>();
        payload.Should().NotBeNull();
        payload!.ChampionId.Should().Be(157);
        payload.Patch.Should().Be("16.5");
        payload.Position.Should().Be("MIDDLE");
        payload.TotalGames.Should().Be(8, "only Phantasm's games are in scope");
        payload.TotalWins.Should().Be(5);

        payload.Builds.Should().ContainSingle("Phantasm only ran one (firstItem, keystone) build");
        var build = payload.Builds[0];
        build.FirstItemId.Should().Be(3153);
        build.PrimaryKeystoneId.Should().Be(8008);
        build.Games.Should().Be(8);
        build.WinRate.Should().BeApproximately(5d / 8d, 1e-9);
    }

    [Fact]
    public async Task GetPlayerChampion_returns_the_same_contract_as_the_global_page()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var scoped = await client.GetAsync("/truemains/Phantasm-EUW1/champions/157");
        scoped.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await scoped.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Top-level contract must match ChampionResponse exactly so the page
        // and composable can be reused with only the data source swapped.
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["championId", "patch", "position", "totalGames", "totalWins", "builds"]);

        foreach (var element in root.GetProperty("builds").EnumerateArray())
        {
            element.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
                ["firstItemId", "primaryKeystoneId", "games", "pickRate", "winRate",
                 "core", "variations", "buildTree", "runePages"]);
        }
    }

    [Fact]
    public async Task GetPlayerChampion_returns_404_below_the_minimum_games_floor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Champion 103 has a single Phantasm game seeded — under the 5-game
        // floor — so the scoped endpoint must report it as "not enough data".
        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/103");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPlayerChampion_returns_404_for_unknown_player()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Nobody-EUW1/champions/157");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPlayerChampion_returns_404_for_champion_the_player_never_mained()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // 9999 has no scope for any account — the player simply has no data.
        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/9999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPlayerChampion_with_no_patch_falls_back_to_the_latest_patch_above_the_floor()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(BuildAccount(AccountOneId, "EUW1", "phantasm-puuid", "Phantasm", "EUW1", now));
            await db.SaveChangesAsync();

            await new ChampionAggregateSeeder()
                // Latest patch (16.6) is thin — 3 games, below the 5-game floor.
                .AddPatternWithRune(AccountOneId, 157, "16.6", "EUW1", 420, "MIDDLE",
                    summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                    buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                    primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                    games: 3, wins: 2, aggregatedAtUtc: now.AddMinutes(-5))
                // Previous patch (16.5) has a usable sample — 8 games.
                .AddPatternWithRune(AccountOneId, 157, "16.5", "EUW1", 420, "MIDDLE",
                    summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                    buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                    primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                    games: 8, wins: 5, aggregatedAtUtc: now.AddMinutes(-10))
                .SaveAsync(db);
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // No patch requested: the latest patch (16.6) is below the floor, so a
        // player view falls back to the newest patch that clears it (16.5)
        // instead of 404-ing as if the main had no data.
        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/157");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ChampionResponse>();
        payload!.Patch.Should().Be("16.5");
        payload.TotalGames.Should().Be(8, "the thin latest patch is skipped for the newest one above the floor");
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(AccountOneId, "EUW1", "phantasm-puuid", "Phantasm", "EUW1", now),
            BuildAccount(AccountTwoId, "EUW1", "rival-puuid", "Rival", "EUW1", now));
        await db.SaveChangesAsync();

        await new ChampionAggregateSeeder()
            // Phantasm on 157: 8 games, 5 wins, all 3153 + Lethal Tempo (8008).
            .AddPatternWithRune(AccountOneId, 157, "16.5", "EUW1", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 8, wins: 5, aggregatedAtUtc: now.AddMinutes(-10))
            // Rival on 157: 6 games, 3 wins, all 6673 + Conqueror (8010). Same
            // champion, different account — must NOT leak into Phantasm's view.
            .AddPatternWithRune(AccountTwoId, 157, "16.5", "EUW1", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [6673, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8010, secondaryStyleId: 8400,
                games: 6, wins: 3, aggregatedAtUtc: now.AddMinutes(-9))
            // Phantasm on 103: a single game — under the min-games floor.
            .AddPatternWithRune(AccountOneId, 103, "16.5", "EUW1", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 7, skillOrderKey: "Q-W-E",
                buildItems: [6655, 3020, 3157], bootsItemId: 3020,
                primaryStyleId: 8200, primaryKeystoneId: 8214, secondaryStyleId: 8100,
                games: 1, wins: 1, aggregatedAtUtc: now.AddMinutes(-8))
            .SaveAsync(db);
    }

    private static RiotAccount BuildAccount(
        Guid id, string platformId, string puuid, string gameName, string tagLine, DateTime now)
        => new()
        {
            Id = id,
            PlatformId = platformId,
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            SummonerId = $"{gameName}-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            LastMatchIngestAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1)
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
}
