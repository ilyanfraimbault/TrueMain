using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionItemTimingsApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";

    private const int Boots = 1001;       // bought @ 300s every game
    private const int CoreItem = 3153;    // bought @ 600s every game
    private const int RareItem = 2055;    // bought in only 5 games (below the floor)

    private readonly PostgresFixture _fixture;

    public ChampionItemTimingsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionItemTimingsAsync_AveragesFirstPurchase_OrderedEarliestFirst()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedItemTimingsAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/item-timings?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var timings = await response.Content.ReadFromJsonAsync<ChampionItemTimingsResponse>();
        timings.Should().NotBeNull();
        timings!.ChampionId.Should().Be(Champion);

        // Boots (300s) before the core item (600s); the below-floor item is dropped.
        timings.Items.Select(i => i.ItemId).Should().Equal(Boots, CoreItem);

        var boots = timings.Items.Single(i => i.ItemId == Boots);
        boots.Games.Should().Be(12);
        boots.AvgSeconds.Should().BeApproximately(300, 1e-6); // first purchase wins over the later re-buy

        var core = timings.Items.Single(i => i.ItemId == CoreItem);
        core.Games.Should().Be(12);
        core.AvgSeconds.Should().BeApproximately(600, 1e-6);

        timings.Items.Should().NotContain(i => i.ItemId == RareItem, "5 games is below the sample floor");
    }

    [Fact]
    public async Task GetChampionItemTimingsAsync_FiltersToRequestedPatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedItemTimingsAsync(games: 12); // all on 16.4.521.123

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var onPatch = await client.GetAsync($"/champions/{Champion}/item-timings?position={Position}&patch=16.4");
        onPatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var matched = await onPatch.Content.ReadFromJsonAsync<ChampionItemTimingsResponse>();
        matched!.Patch.Should().Be("16.4");
        matched.Items.Should().NotBeEmpty();

        var offPatch = await client.GetAsync($"/champions/{Champion}/item-timings?position={Position}&patch=16.5");
        offPatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var missed = await offPatch.Content.ReadFromJsonAsync<ChampionItemTimingsResponse>();
        missed!.Items.Should().BeEmpty("no games were seeded on 16.5");
    }

    [Fact]
    public async Task GetChampionItemTimingsAsync_FiltersToRequestedEloBracket()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBracketedItemTimingsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // ALL sums both cohorts (12 Gold + 12 Iron), each buying Boots then core.
        var all = await client.GetFromJsonAsync<ChampionItemTimingsResponse>(
            $"/champions/{Champion}/item-timings?position={Position}");
        all!.Items.Single(i => i.ItemId == Boots).Games.Should().Be(24);

        // A bare Gold filter counts only the Gold-stamped games.
        var gold = await client.GetFromJsonAsync<ChampionItemTimingsResponse>(
            $"/champions/{Champion}/item-timings?position={Position}&eloBracket=GOLD");
        gold!.Items.Single(i => i.ItemId == Boots).Games.Should().Be(12, "only the Gold-stamped games count");

        // GOLD_PLUS unions Gold and above; Iron is below and drops out.
        var goldPlus = await client.GetFromJsonAsync<ChampionItemTimingsResponse>(
            $"/champions/{Champion}/item-timings?position={Position}&eloBracket=GOLD_PLUS");
        goldPlus!.Items.Single(i => i.ItemId == Boots).Games.Should().Be(12, "Iron is below Gold and drops out");
    }

    [Fact]
    public async Task GetChampionItemTimingsAsync_ReturnsEmptyWhenNoGames()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/item-timings?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var timings = await response.Content.ReadFromJsonAsync<ChampionItemTimingsResponse>();
        timings!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChampionItemTimingsAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/item-timings?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedItemTimingsAsync(int games)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("TimingsMain")
            .WithTagLine("KR1")
            .WithPuuid("timings-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < games; i++)
        {
            var matchId = $"m-timings-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion("16.4.521.123")
                .Build());

            var events = new List<ItemEvent>
            {
                Purchase(Boots, 300_000),
                Purchase(CoreItem, 600_000),
            };
            // First game re-buys the boots later — MIN must keep the 300s purchase.
            if (i == 0)
            {
                events.Add(Purchase(Boots, 900_000));
            }

            // A destroy event must never be counted as a purchase.
            events.Add(new ItemEvent { TimestampMs = 650_000, EventType = "ITEM_DESTROYED", ItemId = CoreItem });

            // The rare item is bought in only the first 5 games — below the floor.
            if (i < 5)
            {
                events.Add(Purchase(RareItem, 120_000));
            }

            db.MatchParticipants.Add(Participant(matchId, account.Id, events));
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds two cohorts for one tracked account — 12 Gold games and 12 Iron games,
    /// each buying Boots @300s and the core item @600s — so the elo-bracket filter
    /// on the champion side can be exercised.
    /// </summary>
    private async Task SeedBracketedItemTimingsAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("TimingsBracket")
            .WithTagLine("KR1")
            .WithPuuid("timings-bracket-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        AddTimingGames(db, "gold", 12, account.Id, EloBracket.Gold);
        AddTimingGames(db, "iron", 12, account.Id, EloBracket.Iron);

        await db.SaveChangesAsync();
    }

    private static void AddTimingGames(
        Data.TrueMainDbContext db, string prefix, int games, Guid accountId, string eloBracket)
    {
        for (var i = 0; i < games; i++)
        {
            var matchId = $"m-timings-{prefix}-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion("16.4.521.123")
                .Build());

            var events = new List<ItemEvent> { Purchase(Boots, 300_000), Purchase(CoreItem, 600_000) };
            db.MatchParticipants.Add(Participant(matchId, accountId, events, eloBracket));
        }
    }

    private static ItemEvent Purchase(int itemId, int timestampMs)
        => new() { TimestampMs = timestampMs, EventType = "ITEM_PURCHASED", ItemId = itemId };

    private static MatchParticipant Participant(
        string matchId, Guid accountId, List<ItemEvent> itemEvents, string eloBracket = "")
        => new()
        {
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = $"puuid-{matchId}",
            RiotAccountId = accountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = Champion,
            TeamId = 100,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            Win = true,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            EloBracket = eloBracket,
            ItemEvents = itemEvents,
            SkillEvents = []
        };

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinMatchupGames", "10"),
                new KeyValuePair<string, string?>("ChampionsList:MinPlayerMatchupGames", "3"),
            ]);
}
