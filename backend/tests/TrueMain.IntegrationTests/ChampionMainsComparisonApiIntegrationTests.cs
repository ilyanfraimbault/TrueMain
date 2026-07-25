using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// <c>GET /champions/{id}/mains-comparison</c> (#528): one Riot account's games
/// on a champion set against the champion's mains. The endpoint never calls
/// Riot, so every case here is a database-only lookup.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionMainsComparisonApiIntegrationTests
{
    private const int QueueId = 420; // Ranked Solo/Duo, matched by MainAnalysis:QueueId below.
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";
    private const string Patch = "16.4.521.123";

    // The compared account is itself a main of the champion, so the pool must
    // exclude it — see ComparesAgainstThePoolOfMains.
    private const string PlayerName = "CompareMe";
    private const string MainOneName = "MainOne";
    private const string MainTwoName = "MainTwo";
    private const string Tag = "KR1";

    private readonly PostgresFixture _fixture;

    public ChampionMainsComparisonApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetMainsComparisonAsync_ReturnsUnknownAccountForAnUntrackedRiotId()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // The Riot API key is dead and ingestion is stopped, so an account we
        // have never ingested simply cannot be compared. That is a normal 200
        // answer with an explanatory status — never an error, never a 404.
        var response = await client.GetAsync(
            $"/champions/{Champion}/mains-comparison?account={Uri.EscapeDataString("Nobody#KR1")}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var comparison = await response.Content.ReadFromJsonAsync<ChampionMainsComparisonResponse>();
        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(ChampionComparisonStatus.UnknownAccount);
        comparison.Player.Should().BeNull();
        comparison.Mains.Should().BeNull();
        comparison.MinGames.Should().Be(5, "the configured ChampionsList:MinComparisonGames floor is echoed back");
    }

    [Fact]
    public async Task GetMainsComparisonAsync_ComparesAgainstThePoolOfMains()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}&position={Position}&patch=16.4");
        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(ChampionComparisonStatus.Ok);
        comparison.Patch.Should().Be("16.4");
        comparison.Position.Should().Be(Position);

        // Player: 6 MIDDLE games on 16.4 (4 won), 5/4/6 and 12 000 gold on
        // 184 CS over 30-minute games.
        comparison.Player.Should().NotBeNull();
        var player = comparison.Player!;
        player.Identity.Should().NotBeNull();
        player.Identity!.GameName.Should().Be(PlayerName);
        player.Players.Should().Be(1);
        player.Games.Should().Be(6);
        player.Wins.Should().Be(4);
        player.WinRate.Should().BeApproximately(4d / 6d, 1e-9);
        player.Kills.Should().BeApproximately(5d, 1e-9);
        player.Deaths.Should().BeApproximately(4d, 1e-9);
        player.Assists.Should().BeApproximately(6d, 1e-9);
        player.Kda.Should().BeApproximately((30d + 36d) / 24d, 1e-9);
        player.CsPerMin.Should().BeApproximately(6d * 184d / (6d * 30d), 1e-9);
        player.GoldPerMin.Should().BeApproximately(12_000d / 30d, 1e-9);
        player.GoldPerGame.Should().BeApproximately(12_000d, 1e-9);
        player.SampleMet.Should().BeTrue();

        // Mains pool: MainOne (8 games on 16.4, 6 won) + MainTwo (5 games, 2
        // won). The compared account is flagged a main of the champion too, but
        // must be excluded from the yardstick it is measured against — so 13
        // games, not 19, and two contributing players, not three.
        comparison.Mains.Should().NotBeNull();
        var mains = comparison.Mains!;
        mains.Identity.Should().BeNull("the aggregate column has no single owner");
        mains.Players.Should().Be(2);
        mains.Games.Should().Be(13);
        mains.Wins.Should().Be(8);
        mains.WinRate.Should().BeApproximately(8d / 13d, 1e-9);

        // Kills 8*8 + 5*3 = 79, deaths 8*2 + 5*6 = 46, assists 8*4 + 5*8 = 72.
        mains.Kills.Should().BeApproximately(79d / 13d, 1e-9);
        mains.Deaths.Should().BeApproximately(46d / 13d, 1e-9);
        mains.Assists.Should().BeApproximately(72d / 13d, 1e-9);
        mains.Kda.Should().BeApproximately((79d + 72d) / 46d, 1e-9);

        // CS 8*250 + 5*150 = 2750 over 8*30 + 5*25 = 365 minutes; gold
        // 8*15 000 + 5*10 000 = 170 000 over the same minutes / 13 games.
        mains.CsPerMin.Should().BeApproximately(2_750d / 365d, 1e-9);
        mains.GoldPerMin.Should().BeApproximately(170_000d / 365d, 1e-9);
        mains.GoldPerGame.Should().BeApproximately(170_000d / 13d, 1e-9);
        mains.SampleMet.Should().BeTrue();
    }

    [Fact]
    public async Task GetMainsComparisonAsync_WithoutAPatchSpansEveryStoredPatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // MainOne also has 2 games on 16.5 that the pinned 16.4 slice drops.
        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}&position={Position}");
        comparison.Should().NotBeNull();
        comparison!.Patch.Should().BeNull("no patch was pinned");
        comparison.Mains!.Games.Should().Be(15, "the two 16.5 games join the 13 on 16.4");
        comparison.Mains.Wins.Should().Be(8, "both extra games were losses");
    }

    [Fact]
    public async Task GetMainsComparisonAsync_WithMainTargetsThatSinglePlayer()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}"
            + $"&main={Uri.EscapeDataString($"{MainTwoName}#{Tag}")}&position={Position}&patch=16.4");
        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(ChampionComparisonStatus.Ok);

        comparison.Mains.Should().NotBeNull();
        var mains = comparison.Mains!;
        mains.Identity.Should().NotBeNull();
        mains.Identity!.GameName.Should().Be(MainTwoName);
        mains.Players.Should().Be(1);
        mains.Games.Should().Be(5, "only the targeted main's games count");
        mains.Wins.Should().Be(2);
    }

    [Fact]
    public async Task GetMainsComparisonAsync_ReturnsUnknownTargetButKeepsThePlayerColumn()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Only the yardstick is missing here: the compared account resolved, so
        // its column must still come back populated. Player's contract is
        // "null only when the Riot ID is unknown to us" — an unresolvable
        // `main` is a statement about the target, not about the player.
        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}"
            + $"&main={Uri.EscapeDataString("Ghost#KR1")}&position={Position}&patch=16.4");
        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(ChampionComparisonStatus.UnknownTarget);
        comparison.Mains.Should().BeNull();

        comparison.Player.Should().NotBeNull();
        var player = comparison.Player!;
        player.Identity.Should().NotBeNull();
        player.Identity!.GameName.Should().Be(PlayerName);
        player.Games.Should().Be(6, "the player's own slice is unaffected by an unknown target");
        player.Wins.Should().Be(4);
        player.WinRate.Should().BeApproximately(4d / 6d, 1e-9);
        player.SampleMet.Should().BeTrue();
    }

    [Fact]
    public async Task GetMainsComparisonAsync_TargetsAnyTrackedAccountNotJustAMain()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // `main` resolves any account we hold — being flagged a main of the
        // champion is deliberately not required, so a caller can measure against
        // a specific rival. NotAMain has 4 recorded games on the champion yet
        // never joins the pool column; targeting them directly still works.
        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}"
            + $"&main={Uri.EscapeDataString($"NotAMain#{Tag}")}&position={Position}&patch=16.4");
        comparison.Should().NotBeNull();

        comparison!.Mains.Should().NotBeNull();
        var mains = comparison.Mains!;
        mains.Identity!.GameName.Should().Be("NotAMain");
        mains.Players.Should().Be(1);
        mains.Games.Should().Be(4);
    }

    [Fact]
    public async Task GetMainsComparisonAsync_AcceptsTheSlugFormAndIgnoresCase()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // The URL slug form ("Name-TAG") and a different casing must land on the
        // same account as the typed "Name#TAG" form.
        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName.ToLowerInvariant()}-{Tag.ToLowerInvariant()}")}"
            + $"&position={Position}&patch=16.4");
        comparison.Should().NotBeNull();
        comparison!.Player.Should().NotBeNull();
        comparison.Player!.Identity!.GameName.Should().Be(PlayerName);
        comparison.Player.Games.Should().Be(6);
    }

    [Fact]
    public async Task GetMainsComparisonAsync_DoesNotTreatWildcardsInTheNameAsPatterns()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // A bare '%' must be matched literally, not as an ILIKE wildcard that
        // would resolve to whichever account happens to sort first.
        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison?account={Uri.EscapeDataString($"%#{Tag}")}");
        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(ChampionComparisonStatus.UnknownAccount);
    }

    [Fact]
    public async Task GetMainsComparisonAsync_FlagsAThinPlayerSample()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedThinPlayerSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Two games is below the floor: the columns still come back with their
        // real counts so the caller can say how far the sample is from the bar.
        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}&position={Position}");
        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(ChampionComparisonStatus.InsufficientSample);
        comparison.Player!.Games.Should().Be(2);
        comparison.Player.SampleMet.Should().BeFalse();
        comparison.Mains!.Games.Should().Be(8);
        comparison.Mains.SampleMet.Should().BeTrue();
    }

    [Fact]
    public async Task GetMainsComparisonAsync_ReturnsBadRequestWithoutAnAccount()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/mains-comparison");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("NoSeparator")]
    [InlineData("#OnlyTag")]
    [InlineData("OnlyName#")]
    [InlineData("Two#Hash#es")]
    public async Task GetMainsComparisonAsync_ReturnsBadRequestForAMalformedAccount(string account)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Malformed input is a client error, kept distinct from the 200 +
        // UNKNOWN_ACCOUNT that a *well-formed* Riot ID we don't hold returns —
        // otherwise a typo would render as "we don't track this account yet".
        var response = await client.GetAsync(
            $"/champions/{Champion}/mains-comparison?account={Uri.EscapeDataString(account)}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMainsComparisonAsync_ReturnsBadRequestForAnOverlongAccount()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Past NameTagParser.MaxRiotIdLength: junk or abuse, rejected before it
        // ever reaches a query.
        var overlong = $"{new string('a', 80)}#{Tag}";
        var response = await client.GetAsync(
            $"/champions/{Champion}/mains-comparison?account={Uri.EscapeDataString(overlong)}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMainsComparisonAsync_ReturnsBadRequestForAMalformedMain()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedComparisonSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // A malformed target is the same client error as a malformed account —
        // UNKNOWN_TARGET is reserved for a well-formed Riot ID we don't hold.
        var response = await client.GetAsync(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}&main=NoSeparator");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMainsComparisonAsync_ReturnsBadRequestForAnInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/champions/{Champion}/mains-comparison"
            + $"?account={Uri.EscapeDataString($"{PlayerName}#{Tag}")}&position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Seeds the shared sample: the compared account (6 MIDDLE games on 16.4,
    /// 4 won) plus two mains — MainOne (8 on 16.4, 6 won, and 2 more on 16.5,
    /// both lost) and MainTwo (5 games, 2 won, 25-minute games). Four kinds of
    /// decoy must never reach either column: a TOP game, a wrong-queue game, an
    /// untracked participant row, and a tracked account that is not a main of
    /// the champion.
    /// </summary>
    private async Task SeedComparisonSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var player = Account(PlayerName, "compare-me-puuid");
        var mainOne = Account(MainOneName, "main-one-puuid");
        var mainTwo = Account(MainTwoName, "main-two-puuid");
        var notAMain = Account("NotAMain", "not-a-main-puuid");
        db.RiotAccounts.AddRange(player, mainOne, mainTwo, notAMain);

        // The compared account is a main of the champion too — the pool must
        // still exclude it.
        db.MainChampionStats.AddRange(
            MainStat(player, games: 6),
            MainStat(mainOne, games: 10),
            MainStat(mainTwo, games: 5));

        for (var i = 0; i < 6; i++)
        {
            AddGame(db, $"cmp-player-{i}", Patch, QueueId, Position, win: i < 4, player.Id,
                kills: 5, deaths: 4, assists: 6, gold: 12_000, minions: 180, monsters: 4, durationSeconds: 1_800);
        }

        for (var i = 0; i < 8; i++)
        {
            AddGame(db, $"cmp-one-{i}", Patch, QueueId, Position, win: i < 6, mainOne.Id,
                kills: 8, deaths: 2, assists: 4, gold: 15_000, minions: 240, monsters: 10, durationSeconds: 1_800);
        }

        // Two later-patch games for MainOne: dropped by ?patch=16.4, kept when
        // no patch is pinned.
        for (var i = 0; i < 2; i++)
        {
            AddGame(db, $"cmp-one-next-{i}", "16.5.1", QueueId, Position, win: false, mainOne.Id,
                kills: 8, deaths: 2, assists: 4, gold: 15_000, minions: 240, monsters: 10, durationSeconds: 1_800);
        }

        for (var i = 0; i < 5; i++)
        {
            AddGame(db, $"cmp-two-{i}", Patch, QueueId, Position, win: i < 2, mainTwo.Id,
                kills: 3, deaths: 6, assists: 8, gold: 10_000, minions: 150, monsters: 0, durationSeconds: 1_500);
        }

        // Decoy 1: another lane (dropped by the position filter).
        AddGame(db, "cmp-decoy-lane", Patch, QueueId, "TOP", win: true, mainOne.Id,
            kills: 99, deaths: 0, assists: 99, gold: 99_000, minions: 999, monsters: 0, durationSeconds: 1_800);

        // Decoy 2: a different queue.
        AddGame(db, "cmp-decoy-queue", Patch, queueId: 400, Position, win: true, mainOne.Id,
            kills: 99, deaths: 0, assists: 99, gold: 99_000, minions: 999, monsters: 0, durationSeconds: 1_800);

        // Decoy 3: an untracked participant (no RiotAccountId) — in neither column.
        AddGame(db, "cmp-decoy-anon", Patch, QueueId, Position, win: true, riotAccountId: null,
            kills: 99, deaths: 0, assists: 99, gold: 99_000, minions: 999, monsters: 0, durationSeconds: 1_800);

        // Decoy 4: a tracked account with no main row on this champion — real
        // games, but not part of the champion's mains.
        for (var i = 0; i < 4; i++)
        {
            AddGame(db, $"cmp-decoy-notmain-{i}", Patch, QueueId, Position, win: true, notAMain.Id,
                kills: 99, deaths: 0, assists: 99, gold: 99_000, minions: 999, monsters: 0, durationSeconds: 1_800);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a below-floor player (2 games) against an above-floor main (8), so
    /// the response is INSUFFICIENT_SAMPLE with only the player column flagged.
    /// </summary>
    private async Task SeedThinPlayerSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var player = Account(PlayerName, "thin-player-puuid");
        var mainOne = Account(MainOneName, "thin-main-puuid");
        db.RiotAccounts.AddRange(player, mainOne);
        db.MainChampionStats.Add(MainStat(mainOne, games: 8));

        for (var i = 0; i < 2; i++)
        {
            AddGame(db, $"thin-player-{i}", Patch, QueueId, Position, win: true, player.Id,
                kills: 5, deaths: 4, assists: 6, gold: 12_000, minions: 180, monsters: 4, durationSeconds: 1_800);
        }

        for (var i = 0; i < 8; i++)
        {
            AddGame(db, $"thin-main-{i}", Patch, QueueId, Position, win: i < 5, mainOne.Id,
                kills: 8, deaths: 2, assists: 4, gold: 15_000, minions: 240, monsters: 10, durationSeconds: 1_800);
        }

        await db.SaveChangesAsync();
    }

    private static RiotAccount Account(string gameName, string puuid)
        => new RiotAccountBuilder()
            .WithGameName(gameName)
            .WithTagLine(Tag)
            .WithPuuid(puuid)
            .Build();

    private static MainChampionStat MainStat(RiotAccount account, int games)
        => new()
        {
            PlatformId = account.PlatformId,
            Puuid = account.Puuid,
            ChampionId = Champion,
            TotalMatches = games,
            ChampionMatches = games,
            PlayRate = 1.0,
            IsMain = true,
            PrimaryPosition = Position,
            CalculatedAtUtc = DateTime.UtcNow
        };

    private static void AddGame(
        Data.TrueMainDbContext db,
        string matchId,
        string gameVersion,
        int queueId,
        string teamPosition,
        bool win,
        Guid? riotAccountId,
        int kills,
        int deaths,
        int assists,
        int gold,
        int minions,
        int monsters,
        int durationSeconds)
    {
        var match = new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(queueId)
            .WithGameVersion(gameVersion)
            .WithGameDurationSeconds(durationSeconds)
            .Build();
        db.Matches.Add(match);

        db.MatchParticipants.Add(new MatchParticipant
        {
            MatchId = match.Id,
            ParticipantId = 1,
            Puuid = $"puuid-{matchId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = Champion,
            TeamId = 100,
            TeamPosition = teamPosition,
            IndividualPosition = teamPosition,
            Lane = teamPosition,
            Role = "SOLO",
            Win = win,
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
            GoldEarned = gold,
            TotalMinionsKilled = minions,
            NeutralMinionsKilled = monsters,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 4,
            Summoner2Id = 12,
            ItemEvents = [],
            SkillEvents = []
        });
    }

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
                new KeyValuePair<string, string?>("ChampionsList:MinComparisonGames", "5"),
            ]);
}
