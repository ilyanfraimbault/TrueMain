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
    public async Task GetChampionMatchupsAsync_WithOpponent_ReturnsThatMatchupBelowTheFloor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Only one Yone-vs-Talon game is seeded — far below the leaderboard
        // floor, so it never appears in the unfiltered list. A deliberate
        // ?opponent lookup drops the floor to one game and returns just that
        // head-to-head (and nothing else, not even the above-floor Zed line).
        var response = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matchups = await response.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        matchups.Should().NotBeNull();
        var talon = matchups!.Matchups.Should().ContainSingle().Subject;
        talon.OpponentChampionId.Should().Be(OtherOpponent);
        talon.Games.Should().Be(1);
        talon.Wins.Should().Be(1);
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
        var nameTag = await SeedScopedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // The account owns 11 of the Yone-vs-Zed games (6 won); a second tracked
        // account's 5 games and the 3 anonymous games must not leak into its slice.
        var response = await client.GetAsync(
            $"/truemains/{nameTag}/champions/{Champion}/matchups?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matchups = await response.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        matchups.Should().NotBeNull();

        var zed = matchups!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.Games.Should().Be(11, "only this account's Yone-vs-Zed games count");
        zed.Wins.Should().Be(6);

        // The global pool keeps both tracked accounts (11 + 5) yet still drops
        // the anonymous games — so the player slice is a strict subset of it.
        var globalResponse = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}");
        globalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var global = await globalResponse.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        var globalZed = global!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        globalZed.Games.Should().Be(16, "both tracked accounts count globally; the anonymous games never do");
    }

    [Fact]
    public async Task GetPlayerChampionMatchupsAsync_UsesLowerPerPlayerFloor()
    {
        await _fixture.ResetDatabaseAsync();
        var nameTag = await SeedPlayerFloorSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Five owned Yone-vs-Zed games: above the per-player floor (3) yet below
        // the global floor (10). The player slice lists Zed...
        var playerResponse = await client.GetAsync(
            $"/truemains/{nameTag}/champions/{Champion}/matchups?position={Position}");
        playerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var player = await playerResponse.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        var zed = player!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.Games.Should().Be(5);

        // ...while the global pool (this lone account) drops the same games.
        var globalResponse = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}");
        var global = await globalResponse.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        global!.Matchups.Should().BeEmpty(
            "five games clears the per-player floor but not the global MinMatchupGames floor of 10");
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

    [Fact]
    public async Task GetPlayerChampionMatchupsAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Position is validated before the account lookup, so an unknown player
        // with an unrecognised position is still a 400, not a 404.
        var response = await client.GetAsync(
            $"/truemains/Nobody-KR1/champions/{Champion}/matchups?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Seeds a global matchup sample (one tracked account): 12 Yone-vs-Zed lane games
    /// (10 on 16.4, 2 on 16.5; 7 won overall), plus four kinds of decoys that
    /// must never count — Zed on Yone's own team, Zed in another lane, a
    /// wrong-queue Yone-vs-Zed game, and a single Yone-vs-Talon lane game.
    /// </summary>
    private async Task SeedMatchupSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        // The global matchups query counts only tracked accounts (RiotAccountId
        // set) to match the champion page's aggregation, so the Yone side must
        // belong to a tracked account for any of these games to be counted. The
        // decoys share that account too, so each is still excluded by its own
        // rule (team / lane / queue / opponent), not merely by this filter.
        var account = new RiotAccountBuilder()
            .WithGameName("MatchupGlobal")
            .WithTagLine("KR1")
            .WithPuuid("matchup-global-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < 12; i++)
        {
            var patch = i < 10 ? "16.4.521.123" : "16.5.1";
            AddLaneMatchup(db, $"m-zed-{i}", patch, QueueId, yoneWins: i < 7, Opponent, yoneAccountId: account.Id);
        }

        // Decoy 1: Zed on the SAME team as Yone (opposite-team rule excludes it).
        var sameTeam = new MatchBuilder().WithId("m-sameteam").WithQueueId(QueueId).Build();
        db.Matches.Add(sameTeam);
        db.MatchParticipants.Add(Participant(sameTeam.Id, 1, Champion, teamId: 100, Position, win: true, riotAccountId: account.Id));
        db.MatchParticipants.Add(Participant(sameTeam.Id, 2, Opponent, teamId: 100, Position, win: true));

        // Decoy 2: Zed present but in a different lane (same-position rule excludes it).
        var otherLane = new MatchBuilder().WithId("m-otherlane").WithQueueId(QueueId).Build();
        db.Matches.Add(otherLane);
        db.MatchParticipants.Add(Participant(otherLane.Id, 1, Champion, teamId: 100, Position, win: true, riotAccountId: account.Id));
        db.MatchParticipants.Add(Participant(otherLane.Id, 2, Opponent, teamId: 200, "TOP", win: true));

        // Decoy 3: a Yone-vs-Zed lane game on a different queue (queue filter excludes it).
        AddLaneMatchup(db, "m-wrongqueue", "16.4.521.123", queueId: 400, yoneWins: true, Opponent, yoneAccountId: account.Id);

        // Decoy 4: a single Yone-vs-Talon lane game (different opponent; also
        // below the floor on its own, used by the not-enough-data test).
        AddLaneMatchup(db, "m-talon", "16.4.521.123", QueueId, yoneWins: true, OtherOpponent, yoneAccountId: account.Id);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a player-scoped sample for the account under test: 11 Yone-vs-Zed
    /// lane games it owns (6 won), plus two kinds of games its slice must drop
    /// while the global pool keeps the tracked one — 5 Yone-vs-Zed games owned
    /// by a <em>second tracked account</em> (these exercise real inter-account
    /// isolation, not just the null-account filter) and 3 anonymous Yone-vs-Zed
    /// games (counted by neither scope).
    /// </summary>
    private async Task<string> SeedScopedMatchupSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("MatchupMain")
            .WithTagLine("KR1")
            .WithPuuid("matchup-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        var otherAccount = new RiotAccountBuilder()
            .WithGameName("MatchupOther")
            .WithTagLine("KR1")
            .WithPuuid("matchup-other-puuid")
            .Build();
        db.RiotAccounts.Add(otherAccount);

        for (var i = 0; i < 11; i++)
        {
            AddLaneMatchup(db, $"ms-owned-{i}", "16.4.521.123", QueueId, yoneWins: i < 6, Opponent,
                yoneAccountId: account.Id);
        }

        // A second tracked account's games: part of the global pool, never this
        // player's slice.
        for (var i = 0; i < 5; i++)
        {
            AddLaneMatchup(db, $"ms-other-{i}", "16.4.521.123", QueueId, yoneWins: true, Opponent,
                yoneAccountId: otherAccount.Id);
        }

        // Anonymous games: counted by neither scope (no tracked account).
        for (var i = 0; i < 3; i++)
        {
            AddLaneMatchup(db, $"ms-anon-{i}", "16.4.521.123", QueueId, yoneWins: true, Opponent);
        }

        await db.SaveChangesAsync();
        return $"{account.GameName}-{account.TagLine}";
    }

    /// <summary>
    /// Seeds one tracked account with five Yone-vs-Zed lane games — above the
    /// per-player floor (<c>MinPlayerMatchupGames</c>) but below the global floor
    /// (<c>MinMatchupGames</c>) — so the player slice lists Zed while the global
    /// pool (this lone account) drops it. Returns the player's name tag.
    /// </summary>
    private async Task<string> SeedPlayerFloorSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("MatchupFloor")
            .WithTagLine("KR1")
            .WithPuuid("matchup-floor-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < 5; i++)
        {
            AddLaneMatchup(db, $"mf-zed-{i}", "16.4.521.123", QueueId, yoneWins: i < 3, Opponent,
                yoneAccountId: account.Id);
        }

        await db.SaveChangesAsync();
        return $"{account.GameName}-{account.TagLine}";
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
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinMatchupGames", "10"),
                new KeyValuePair<string, string?>("ChampionsList:MinPlayerMatchupGames", "3"),
            ]);
}
