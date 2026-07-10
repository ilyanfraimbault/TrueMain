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
public sealed class ChampionScalingApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";

    private const int ShortGameSeconds = 1000; // bucket 0 (<20m)
    private const int LongGameSeconds = 2200;  // bucket 4 (35m+)

    private readonly PostgresFixture _fixture;

    public ChampionScalingApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionScalingAsync_BucketsWinRateByDuration_AndComputesIndex()
    {
        await _fixture.ResetDatabaseAsync();
        // Short games: 4/12 won. Long games: 9/12 won → champion scales late.
        await SeedScalingSampleAsync(shortGames: 12, shortWins: 4, longGames: 12, longWins: 9);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/scaling?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var scaling = await response.Content.ReadFromJsonAsync<ChampionScalingResponse>();
        scaling.Should().NotBeNull();
        scaling!.ChampionId.Should().Be(Champion);
        scaling.Position.Should().Be(Position);

        scaling.Buckets.Select(b => b.Bucket).Should().Equal(0, 4);

        var shortBucket = scaling.Buckets.Single(b => b.Bucket == 0);
        shortBucket.Label.Should().Be("<20m");
        shortBucket.Games.Should().Be(12);
        shortBucket.WinRate.Should().BeApproximately(4d / 12d, 1e-9);

        var longBucket = scaling.Buckets.Single(b => b.Bucket == 4);
        longBucket.Label.Should().Be("35m+");
        longBucket.Games.Should().Be(12);
        longBucket.WinRate.Should().BeApproximately(9d / 12d, 1e-9);

        scaling.ScalingIndex.Should().BeApproximately(9d / 12d - 4d / 12d, 1e-9);
    }

    [Fact]
    public async Task GetChampionScalingAsync_DropsThinBuckets_AndNullsIndexBelowTwoBuckets()
    {
        await _fixture.ResetDatabaseAsync();
        // Only the short bucket clears the floor of 10; the long bucket (5) drops.
        await SeedScalingSampleAsync(shortGames: 12, shortWins: 6, longGames: 5, longWins: 3);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/scaling?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var scaling = await response.Content.ReadFromJsonAsync<ChampionScalingResponse>();
        scaling!.Buckets.Should().ContainSingle(b => b.Bucket == 0);
        scaling.ScalingIndex.Should().BeNull("a single qualifying bucket can't express a trend");
    }

    [Fact]
    public async Task GetChampionScalingAsync_FiltersToRequestedPatch()
    {
        await _fixture.ResetDatabaseAsync();
        // 12 short games, all on 16.4.521.123 (no long games seeded).
        await SeedScalingSampleAsync(shortGames: 12, shortWins: 6, longGames: 0, longWins: 0);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var onPatch = await client.GetAsync($"/champions/{Champion}/scaling?position={Position}&patch=16.4");
        onPatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var matched = await onPatch.Content.ReadFromJsonAsync<ChampionScalingResponse>();
        matched!.Patch.Should().Be("16.4");
        matched.Buckets.Should().ContainSingle(b => b.Bucket == 0);

        var offPatch = await client.GetAsync($"/champions/{Champion}/scaling?position={Position}&patch=16.5");
        offPatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var missed = await offPatch.Content.ReadFromJsonAsync<ChampionScalingResponse>();
        missed!.Buckets.Should().BeEmpty("no games were seeded on 16.5");
        missed.ScalingIndex.Should().BeNull();
    }

    [Fact]
    public async Task GetChampionScalingAsync_ReturnsEmptyWhenNoGames()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/scaling?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var scaling = await response.Content.ReadFromJsonAsync<ChampionScalingResponse>();
        scaling!.Buckets.Should().BeEmpty();
        scaling.ScalingIndex.Should().BeNull();
    }

    [Fact]
    public async Task GetChampionScalingAsync_FiltersToRequestedEloBracket()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBracketedScalingSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // ALL (no bracket) sums both slices: 12 Gold + 12 Iron short games.
        var all = await client.GetFromJsonAsync<ChampionScalingResponse>(
            $"/champions/{Champion}/scaling?position={Position}");
        all!.Buckets.Single(b => b.Bucket == 0).Games.Should().Be(24);

        // A bare Gold filter counts only the Gold-stamped games.
        var gold = await client.GetFromJsonAsync<ChampionScalingResponse>(
            $"/champions/{Champion}/scaling?position={Position}&eloBracket=GOLD");
        var goldBucket = gold!.Buckets.Single(b => b.Bucket == 0);
        goldBucket.Games.Should().Be(12, "only the Gold-stamped games count");
        goldBucket.WinRate.Should().BeApproximately(8d / 12d, 1e-9);

        // GOLD_PLUS unions Gold and every tier above; Iron sits below and drops.
        var goldPlus = await client.GetFromJsonAsync<ChampionScalingResponse>(
            $"/champions/{Champion}/scaling?position={Position}&eloBracket=GOLD_PLUS");
        goldPlus!.Buckets.Single(b => b.Bucket == 0).Games.Should().Be(12, "Iron is below Gold and drops out");
    }

    [Fact]
    public async Task GetChampionScalingAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/scaling?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedScalingSampleAsync(int shortGames, int shortWins, int longGames, int longWins)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("ScalingMain")
            .WithTagLine("KR1")
            .WithPuuid("scaling-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        AddGames(db, "short", ShortGameSeconds, shortGames, shortWins, account.Id);
        AddGames(db, "long", LongGameSeconds, longGames, longWins, account.Id);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds two short-game slices for one tracked account, stamped with distinct
    /// elo bands so the bracket filter can be exercised: 12 Gold games (8 won) and
    /// 12 Iron games. Both land in bucket 0 and clear the per-bucket floor.
    /// </summary>
    private async Task SeedBracketedScalingSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("ScalingBracket")
            .WithTagLine("KR1")
            .WithPuuid("scaling-bracket-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        AddGames(db, "gold", ShortGameSeconds, games: 12, wins: 8, account.Id, EloBracket.Gold);
        AddGames(db, "iron", ShortGameSeconds, games: 12, wins: 6, account.Id, EloBracket.Iron);

        await db.SaveChangesAsync();
    }

    private static void AddGames(
        Data.TrueMainDbContext db, string prefix, int durationSeconds, int games, int wins, Guid accountId,
        string eloBracket = "")
    {
        for (var i = 0; i < games; i++)
        {
            var matchId = $"m-scaling-{prefix}-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion("16.4.521.123")
                .WithGameDurationSeconds(durationSeconds)
                .Build());

            db.MatchParticipants.Add(
                Participant(matchId, Champion, win: i < wins, riotAccountId: accountId, eloBracket));
        }
    }

    private static MatchParticipant Participant(
        string matchId, int championId, bool win, Guid? riotAccountId, string eloBracket = "")
        => new()
        {
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = $"puuid-{matchId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = 100,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            Win = win,
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
