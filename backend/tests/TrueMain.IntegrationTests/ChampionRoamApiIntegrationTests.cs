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
public sealed class ChampionRoamApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";

    // The seeded champion is a blue-side (team 100) MIDDLE laner. Verified against
    // LolMap: both of these are non-roams for it — own mid lane, and the blue-side
    // (own) jungle, which no longer counts as roaming.
    private static readonly (int X, int Y)[] NonRoam = [(3203, 3208), (6500, 4000)];

    // Red-side (enemy) jungle: a genuine roam for a blue-side laner. Seeded at
    // three timestamps so the cumulative @5/@10/@15 windows each pick up one more.
    private static readonly (int X, int Y) EnemyJungle = (8500, 11000);
    private static readonly int[] RoamTimestampsMs = [200_000, 400_000, 700_000]; // 3:20, 6:40, 11:40

    private readonly PostgresFixture _fixture;

    public ChampionRoamApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionRoamAsync_ComputesPerGameRoamKpWindows()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedRoamSampleAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/roam?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roam = await response.Content.ReadFromJsonAsync<ChampionRoamResponse>();
        roam.Should().NotBeNull();
        roam!.Games.Should().Be(12);
        // Per game: 1 enemy-jungle roam before 5 min, 2 before 10 min, 3 before 15
        // min. The two NonRoam positions (own lane, own jungle) never count.
        roam.RoamKp5.Should().BeApproximately(1d, 1e-9);
        roam.RoamKp10.Should().BeApproximately(2d, 1e-9);
        roam.RoamKp15.Should().BeApproximately(3d, 1e-9);
    }

    [Fact]
    public async Task GetChampionRoamAsync_NullsWindowsBelowSampleFloor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedRoamSampleAsync(games: 5); // below MinMatchupGames floor of 10

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/roam?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roam = await response.Content.ReadFromJsonAsync<ChampionRoamResponse>();
        roam!.RoamKp15.Should().BeNull("five games is below the sample floor");
    }

    [Fact]
    public async Task GetChampionRoamAsync_ExcludesJungle()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/roam?position=JUNGLE");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roam = await response.Content.ReadFromJsonAsync<ChampionRoamResponse>();
        roam!.RoamKp15.Should().BeNull("the roam metric is meaningless for junglers");
    }

    [Fact]
    public async Task GetChampionRoamAsync_FiltersToRequestedPatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedRoamSampleAsync(games: 12); // all on 16.4.521.123

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/roam?position={Position}&patch=16.5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roam = await response.Content.ReadFromJsonAsync<ChampionRoamResponse>();
        roam!.Games.Should().Be(0, "no games on 16.5");
        roam.RoamKp15.Should().BeNull();
    }

    [Fact]
    public async Task GetChampionRoamAsync_FiltersToRequestedEloBracket()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBracketedRoamSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // ALL sums both cohorts: 12 Gold + 12 Iron games, same roam pattern.
        var all = await client.GetFromJsonAsync<ChampionRoamResponse>(
            $"/champions/{Champion}/roam?position={Position}");
        all!.Games.Should().Be(24);

        // A bare Gold filter counts only the Gold-stamped games.
        var gold = await client.GetFromJsonAsync<ChampionRoamResponse>(
            $"/champions/{Champion}/roam?position={Position}&eloBracket=GOLD");
        gold!.Games.Should().Be(12, "only the Gold-stamped games count");
        gold.RoamKp15.Should().BeApproximately(3d, 1e-9);

        // GOLD_PLUS unions Gold and above; Iron is below and drops out.
        var goldPlus = await client.GetFromJsonAsync<ChampionRoamResponse>(
            $"/champions/{Champion}/roam?position={Position}&eloBracket=GOLD_PLUS");
        goldPlus!.Games.Should().Be(12, "Iron is below Gold and drops out");
    }

    [Fact]
    public async Task GetChampionRoamAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/roam?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedRoamSampleAsync(int games)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("RoamMain")
            .WithTagLine("KR1")
            .WithPuuid("roam-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < games; i++)
        {
            var matchId = $"m-roam-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion("16.4.521.123")
                .Build());

            db.MatchParticipants.Add(Participant(matchId, account.Id));

            foreach (var (x, y) in NonRoam)
            {
                db.MatchParticipantKillPositions.Add(KillPosition(matchId, 120_000, x, y));
            }

            foreach (var timestampMs in RoamTimestampsMs)
            {
                db.MatchParticipantKillPositions.Add(KillPosition(matchId, timestampMs, EnemyJungle.X, EnemyJungle.Y));
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds two roam cohorts for one tracked account — 12 Gold games and 12 Iron
    /// games, each with the identical enemy-jungle roam pattern — so the elo-bracket
    /// filter on the champion side can be exercised.
    /// </summary>
    private async Task SeedBracketedRoamSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("RoamBracket")
            .WithTagLine("KR1")
            .WithPuuid("roam-bracket-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        AddRoamGames(db, "gold", 12, account.Id, EloBracket.Gold);
        AddRoamGames(db, "iron", 12, account.Id, EloBracket.Iron);

        await db.SaveChangesAsync();
    }

    private static void AddRoamGames(
        Data.TrueMainDbContext db, string prefix, int games, Guid accountId, string eloBracket)
    {
        for (var i = 0; i < games; i++)
        {
            var matchId = $"m-roam-{prefix}-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion("16.4.521.123")
                .Build());

            db.MatchParticipants.Add(Participant(matchId, accountId, eloBracket));

            foreach (var (x, y) in NonRoam)
            {
                db.MatchParticipantKillPositions.Add(KillPosition(matchId, 120_000, x, y));
            }

            foreach (var timestampMs in RoamTimestampsMs)
            {
                db.MatchParticipantKillPositions.Add(KillPosition(matchId, timestampMs, EnemyJungle.X, EnemyJungle.Y));
            }
        }
    }

    private static MatchParticipantKillPosition KillPosition(string matchId, int timestampMs, int x, int y)
        => new()
        {
            MatchId = matchId,
            ParticipantId = 1,
            TimestampMs = timestampMs,
            X = x,
            Y = y
        };

    private static MatchParticipant Participant(string matchId, Guid accountId, string eloBracket = "")
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
            ItemEvents = [],
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
