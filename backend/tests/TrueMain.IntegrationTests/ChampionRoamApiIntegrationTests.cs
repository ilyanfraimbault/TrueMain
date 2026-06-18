using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
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

    // Verified against LolMap: the first two classify as MidLane (in-lane for
    // MIDDLE), the third as Jungle (out-of-lane).
    private static readonly (int X, int Y)[] InLane = [(3203, 3208), (4000, 4000)];
    private static readonly (int X, int Y) OutOfLane = (6500, 4000);

    private readonly PostgresFixture _fixture;

    public ChampionRoamApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionRoamAsync_ComputesOutOfLaneShare()
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
        roam.KillParticipations.Should().Be(36); // 3 positions per game
        roam.OutOfLaneParticipations.Should().Be(12); // 1 of 3 is jungle
        roam.OutOfLaneShare.Should().BeApproximately(12d / 36d, 1e-9);
    }

    [Fact]
    public async Task GetChampionRoamAsync_NullsShareBelowSampleFloor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedRoamSampleAsync(games: 5); // below MinMatchupGames floor of 10

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/roam?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roam = await response.Content.ReadFromJsonAsync<ChampionRoamResponse>();
        roam!.OutOfLaneShare.Should().BeNull("five games is below the sample floor");
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
        roam.OutOfLaneShare.Should().BeNull();
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

            foreach (var (x, y) in InLane)
            {
                db.MatchParticipantKillPositions.Add(KillPosition(matchId, 120_000, x, y));
            }

            db.MatchParticipantKillPositions.Add(KillPosition(matchId, 200_000, OutOfLane.X, OutOfLane.Y));
        }

        await db.SaveChangesAsync();
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

    private static MatchParticipant Participant(string matchId, Guid accountId)
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
