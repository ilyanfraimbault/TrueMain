using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionMatchupApiIntegrationTests
{
    private const int QueueId = 420; // Ranked Solo/Duo, matched by MainAnalysis:QueueId below.
    private const int Champion = 157; // Yone
    private const int Opponent = 238; // Zed
    private const int OtherOpponent = 91; // Talon — a different MIDDLE opponent
    private const string Position = "MIDDLE";

    private readonly PostgresFixture _fixture;

    public ChampionMatchupApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_CountsOnlyLaneOpponentGames()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matchups = await response.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        matchups.Should().NotBeNull();
        matchups!.ChampionId.Should().Be(Champion);
        matchups.Position.Should().Be(Position);
        matchups.Patch.Should().BeNull("no patch was pinned, so the slice spans every patch");

        // 12 lane-vs-Zed games seeded (7 won). The seeder also adds Zed on the
        // same team, Zed in another lane, a wrong-queue Yone-vs-Zed game, and a
        // single Yone-vs-Talon game — none of which may count toward Zed, and
        // the Talon line sits below the floor so it never appears at all.
        var zed = matchups.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.Games.Should().Be(12);
        zed.Wins.Should().Be(7);
        zed.WinRate.Should().BeApproximately(7d / 12d, 1e-9);

        matchups.Matchups.Should().NotContain(
            m => m.OpponentChampionId == OtherOpponent,
            "the single Yone-vs-Talon game is below the MinMatchupGames floor");
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_FiltersToRequestedPatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // 10 of the 12 Yone-vs-Zed games are on 16.4 (full GameVersion
        // "16.4.521.123"); the patch filter must match the major.minor prefix.
        var response = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}&patch=16.4");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matchups = await response.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        matchups.Should().NotBeNull();
        matchups!.Patch.Should().Be("16.4");

        var zed = matchups.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.Games.Should().Be(10, "only the 16.4 games count; the two 16.5 games drop");
        // The 7 wins are games i=0..6, all on 16.4 (i<10); the two dropped 16.5
        // games (i=10,11) were losses, so every seeded win survives the filter.
        zed.Wins.Should().Be(7);
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_ExcludesOpponentsBelowMinGames()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matchups = await response.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        matchups.Should().NotBeNull();

        // Only one Yone-vs-Talon game is seeded — below the default
        // MinMatchupGames floor of 10 — so Talon must not appear, while the
        // 12-game Zed line (above the floor) is the only entry returned.
        matchups!.Matchups.Should().OnlyContain(m => m.OpponentChampionId == Opponent);
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/champions/{Champion}/matchups?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPlayerChampionMatchupsAsync_ScopesToThatPlayersGames()
    {
        await _fixture.ResetDatabaseAsync();
        var (nameTag, accountId) = await SeedScopedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // The account owns 11 of the global Yone-vs-Zed games (6 won); the
        // remaining anonymous ones must not leak into the player slice.
        var response = await client.GetAsync(
            $"/truemains/{nameTag}/champions/{Champion}/matchups?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matchups = await response.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        matchups.Should().NotBeNull();

        var zed = matchups!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.Games.Should().Be(11, "only this account's Yone-vs-Zed games count");
        zed.Wins.Should().Be(6);

        accountId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPlayerChampionMatchupsAsync_ReturnsNotFoundForUnknownNameTag()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/truemains/Nobody-KR1/champions/{Champion}/matchups?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Seeds a global (account-less) matchup sample: 12 Yone-vs-Zed lane games
    /// (10 on 16.4, 2 on 16.5; 7 won overall), plus four kinds of decoys that
    /// must never count — Zed on Yone's own team, Zed in another lane, a
    /// wrong-queue Yone-vs-Zed game, and a single Yone-vs-Talon lane game.
    /// </summary>
    private async Task SeedMatchupSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        for (var i = 0; i < 12; i++)
        {
            var patch = i < 10 ? "16.4.521.123" : "16.5.1";
            AddLaneMatchup(db, $"m-zed-{i}", patch, QueueId, yoneWins: i < 7, Opponent);
        }

        // Decoy 1: Zed on the SAME team as Yone (opposite-team rule excludes it).
        var sameTeam = new MatchBuilder().WithId("m-sameteam").WithQueueId(QueueId).Build();
        db.Matches.Add(sameTeam);
        db.MatchParticipants.Add(Participant(sameTeam.Id, 1, Champion, teamId: 100, Position, win: true));
        db.MatchParticipants.Add(Participant(sameTeam.Id, 2, Opponent, teamId: 100, Position, win: true));

        // Decoy 2: Zed present but in a different lane (same-position rule excludes it).
        var otherLane = new MatchBuilder().WithId("m-otherlane").WithQueueId(QueueId).Build();
        db.Matches.Add(otherLane);
        db.MatchParticipants.Add(Participant(otherLane.Id, 1, Champion, teamId: 100, Position, win: true));
        db.MatchParticipants.Add(Participant(otherLane.Id, 2, Opponent, teamId: 200, "TOP", win: true));

        // Decoy 3: a Yone-vs-Zed lane game on a different queue (queue filter excludes it).
        AddLaneMatchup(db, "m-wrongqueue", "16.4.521.123", queueId: 400, yoneWins: true, Opponent);

        // Decoy 4: a single Yone-vs-Talon lane game (different opponent; also
        // below the floor on its own, used by the not-enough-data test).
        AddLaneMatchup(db, "m-talon", "16.4.521.123", QueueId, yoneWins: true, OtherOpponent);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a player-scoped sample: 11 Yone-vs-Zed lane games owned by one
    /// account (6 won) plus three anonymous Yone-vs-Zed lane games that belong
    /// to the global pool but not to the account.
    /// </summary>
    private async Task<(string NameTag, Guid AccountId)> SeedScopedMatchupSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("MatchupMain")
            .WithTagLine("KR1")
            .WithPuuid("matchup-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < 11; i++)
        {
            AddLaneMatchup(db, $"ms-owned-{i}", "16.4.521.123", QueueId, yoneWins: i < 6, Opponent,
                yoneAccountId: account.Id);
        }

        for (var i = 0; i < 3; i++)
        {
            AddLaneMatchup(db, $"ms-anon-{i}", "16.4.521.123", QueueId, yoneWins: true, Opponent);
        }

        await db.SaveChangesAsync();
        return ($"{account.GameName}-{account.TagLine}", account.Id);
    }

    /// <summary>
    /// Adds one match with Yone on team 100 and the opponent on team 200, both
    /// at <see cref="Position"/> — the lane-opponent shape the query counts.
    /// </summary>
    private static void AddLaneMatchup(
        Data.TrueMainDbContext db,
        string matchId,
        string gameVersion,
        int queueId,
        bool yoneWins,
        int opponentChampionId,
        Guid? yoneAccountId = null)
    {
        var match = new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(queueId)
            .WithGameVersion(gameVersion)
            .Build();
        db.Matches.Add(match);

        db.MatchParticipants.Add(Participant(
            match.Id, 1, Champion, teamId: 100, Position, win: yoneWins, riotAccountId: yoneAccountId));
        db.MatchParticipants.Add(Participant(
            match.Id, 2, opponentChampionId, teamId: 200, Position, win: !yoneWins));
    }

    private static MatchParticipant Participant(
        string matchId,
        int participantId,
        int championId,
        int teamId,
        string teamPosition,
        bool win,
        Guid? riotAccountId = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = teamPosition,
            IndividualPosition = teamPosition,
            Lane = teamPosition,
            Role = "SOLO",
            Win = win,
            Kills = 5,
            Deaths = 4,
            Assists = 6,
            GoldEarned = 12000,
            TotalMinionsKilled = 180,
            NeutralMinionsKilled = 4,
            ChampLevel = 16,
            Item0 = 3153,
            Item1 = 3006,
            Item2 = 3031,
            Item3 = 0,
            Item4 = 0,
            Item5 = 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5001,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 4,
            Summoner2Id = 12,
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
            fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
}
