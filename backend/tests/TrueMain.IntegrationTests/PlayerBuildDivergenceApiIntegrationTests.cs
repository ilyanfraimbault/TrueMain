using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the "you vs mains" endpoint
/// (<c>GET /truemains/{nameTag}/champions/{championId}/divergence</c>, issue
/// #529). One player and three other mains share a champion slice with
/// deliberately different starters, boots and build paths but the *same* skill
/// order, so the payload has to carry both diverging and matching rows — and
/// the mains side has to exclude the player's own games.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PlayerBuildDivergenceApiIntegrationTests
{
    private static readonly Guid PlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RivalOneId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RivalTwoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid RivalThreeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly PostgresFixture _fixture;

    public PlayerBuildDivergenceApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetDivergence_reports_each_side_dominant_choice()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/157/divergence");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<PlayerBuildDivergenceResponse>();
        payload.Should().NotBeNull();
        payload!.ChampionId.Should().Be(157);
        payload.Patch.Should().Be("16.5");
        payload.Position.Should().Be("MIDDLE");
        payload.PlayerGames.Should().Be(10);
        payload.MainsGames.Should().Be(30, "the player's own 10 games must not count as 'mains'");
        payload.MainsPlayers.Should().Be(3);
        payload.MinSampleMet.Should().BeTrue();
        payload.ReferenceSampleMet.Should().BeTrue();

        payload.Dimensions.Should().HaveCount(4);

        var starter = Dimension(payload, BuildDivergenceDimensions.StarterItems);
        starter.Diverges.Should().BeTrue();
        starter.Player.ItemIds.Should().Equal(1055, 2003);
        starter.Mains.ItemIds.Should().Equal(1054, 2003);
        starter.MainsGamesOnPlayerChoice.Should().Be(0, "no main opened on the player's starter");
        starter.MainsRateOnPlayerChoice.Should().Be(0d);
        starter.MainsWinRateOnPlayerChoice.Should().BeNull("nobody played it, so there is no win rate to show");

        var boots = Dimension(payload, BuildDivergenceDimensions.Boots);
        boots.Diverges.Should().BeTrue();
        boots.Player.ItemIds.Should().Equal(3006);
        boots.Mains.ItemIds.Should().Equal(3047);

        var itemPath = Dimension(payload, BuildDivergenceDimensions.ItemPath);
        itemPath.Diverges.Should().BeTrue();
        itemPath.Player.ItemIds.Should().Equal(3153, 3031, 3072);
        itemPath.Mains.ItemIds.Should().Equal(6673, 3031, 3072);
        itemPath.Player.PickRate.Should().BeApproximately(1d, 1e-9);
        itemPath.Mains.PickRate.Should().BeApproximately(1d, 1e-9);
    }

    [Fact]
    public async Task GetDivergence_keeps_matching_dimensions_and_ranks_them_last()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await (await client.GetAsync("/truemains/Phantasm-EUW1/champions/157/divergence"))
            .Content.ReadFromJsonAsync<PlayerBuildDivergenceResponse>();

        // Both sides max Q → E → W. A card that only ever listed mistakes would
        // read as an indictment, so the matching row is returned — just last.
        var skillOrder = Dimension(payload!, BuildDivergenceDimensions.SkillOrder);
        skillOrder.Diverges.Should().BeFalse();
        skillOrder.Player.Skills.Should().Equal("Q", "E", "W");
        skillOrder.Mains.Skills.Should().Equal("Q", "E", "W");
        skillOrder.MainsRateOnPlayerChoice.Should().BeApproximately(1d, 1e-9);
        skillOrder.MainsWinRateOnPlayerChoice.Should().NotBeNull();

        payload!.Dimensions.Take(3).Should().OnlyContain(dimension => dimension.Diverges);
        payload.Dimensions[^1].Dimension.Should().Be(BuildDivergenceDimensions.SkillOrder);
    }

    [Fact]
    public async Task GetDivergence_withholds_the_comparison_below_the_player_floor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Champion 103: the player has 2 games, well under the 5-game floor,
        // while the mains pool is healthy. A 200 with the counts and no
        // dimensions lets the page say why instead of faking a comparison.
        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/103/divergence");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<PlayerBuildDivergenceResponse>();
        payload!.PlayerGames.Should().Be(2);
        payload.MinSampleMet.Should().BeFalse();
        payload.MinPlayerGames.Should().Be(5, "the page shows the real bar, so the payload has to carry it");
        payload.Dimensions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDivergence_withholds_the_comparison_below_the_mains_floor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Champion 64: the player has a usable sample but only one other main
        // with 5 games is on record — too thin to call "what mains do".
        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/64/divergence");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<PlayerBuildDivergenceResponse>();
        payload!.MinSampleMet.Should().BeTrue();
        payload.ReferenceSampleMet.Should().BeFalse();
        payload.MainsGames.Should().Be(5);
        payload.MinMainsGames.Should().Be(20);
        payload.Dimensions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDivergence_returns_404_for_an_unknown_player()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Nobody-EUW1/champions/157/divergence");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDivergence_returns_404_for_a_champion_the_player_never_played()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/9999/divergence");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDivergence_rejects_an_unrecognised_position()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Phantasm-EUW1/champions/157/divergence?position=BANANA");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static BuildDivergenceReadModel Dimension(
        PlayerBuildDivergenceResponse payload,
        string dimension)
    {
        var row = payload.Dimensions.SingleOrDefault(entry => entry.Dimension == dimension);
        row.Should().NotBeNull($"the payload should carry a '{dimension}' row");
        return row!;
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(PlayerId, "phantasm-puuid", "Phantasm", now),
            BuildAccount(RivalOneId, "rival-one-puuid", "RivalOne", now),
            BuildAccount(RivalTwoId, "rival-two-puuid", "RivalTwo", now),
            BuildAccount(RivalThreeId, "rival-three-puuid", "RivalThree", now));
        await db.SaveChangesAsync();

        var seeder = new ChampionAggregateSeeder();

        // ── Champion 157: the full comparison ───────────────────────────────
        // Player: Doran's Blade start, Berserker's, 3153 → 3031 → 3072, Q-E-W.
        seeder.AddPattern(
            PlayerId, 157, "16.5", "EUW1", 420, "MIDDLE",
            summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
            starterItems: [1055, 2003], starterItemsKey: "1055-2003",
            buildItems: [3153, 3031, 3072], bootsItemId: 3006,
            games: 10, wins: 6, aggregatedAtUtc: now.AddMinutes(-10));

        // Three other mains: Doran's Ring start, Merc Treads, 6673 → 3031 →
        // 3072 — but the SAME skill order, so one row has to come back matching.
        foreach (var rivalId in new[] { RivalOneId, RivalTwoId, RivalThreeId })
        {
            seeder.AddPattern(
                rivalId, 157, "16.5", "EUW1", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
                starterItems: [1054, 2003], starterItemsKey: "1054-2003",
                buildItems: [6673, 3031, 3072], bootsItemId: 3047,
                games: 10, wins: 5, aggregatedAtUtc: now.AddMinutes(-9));
        }

        // ── Champion 103: player sample below the floor ─────────────────────
        seeder.AddPattern(
            PlayerId, 103, "16.5", "EUW1", 420, "MIDDLE",
            summoner1Id: 4, summoner2Id: 7, skillOrderKey: "Q-W-E",
            starterItems: [1055, 2003], starterItemsKey: "1055-2003",
            buildItems: [6655, 3157], bootsItemId: 3020,
            games: 2, wins: 1, aggregatedAtUtc: now.AddMinutes(-8));
        foreach (var rivalId in new[] { RivalOneId, RivalTwoId, RivalThreeId })
        {
            seeder.AddPattern(
                rivalId, 103, "16.5", "EUW1", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 7, skillOrderKey: "Q-W-E",
                starterItems: [1054, 2003], starterItemsKey: "1054-2003",
                buildItems: [6653, 3157], bootsItemId: 3020,
                games: 10, wins: 5, aggregatedAtUtc: now.AddMinutes(-8));
        }

        // ── Champion 64: mains pool below the floor ─────────────────────────
        seeder.AddPattern(
            PlayerId, 64, "16.5", "EUW1", 420, "JUNGLE",
            summoner1Id: 11, summoner2Id: 4, skillOrderKey: "Q-E-W",
            starterItems: [1102, 2003], starterItemsKey: "1102-2003",
            buildItems: [6692, 3071], bootsItemId: 3111,
            games: 9, wins: 5, aggregatedAtUtc: now.AddMinutes(-7));
        seeder.AddPattern(
            RivalOneId, 64, "16.5", "EUW1", 420, "JUNGLE",
            summoner1Id: 11, summoner2Id: 4, skillOrderKey: "Q-W-E",
            starterItems: [1103, 2003], starterItemsKey: "1103-2003",
            buildItems: [6673, 3071], bootsItemId: 3047,
            games: 5, wins: 3, aggregatedAtUtc: now.AddMinutes(-7));

        await seeder.SaveAsync(db);
    }

    private static RiotAccount BuildAccount(Guid id, string puuid, string gameName, DateTime now)
        => new()
        {
            Id = id,
            PlatformId = "EUW1",
            Puuid = puuid,
            GameName = gameName,
            TagLine = "EUW1",
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
