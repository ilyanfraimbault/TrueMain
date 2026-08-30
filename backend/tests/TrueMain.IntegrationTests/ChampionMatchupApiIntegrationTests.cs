using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Map;
using Core.Lol.Ranking;
using Core.Options;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>
    /// Matchup games <see cref="SeedMatchupSampleAsync"/> leaves in the aggregate:
    /// 12 vs Zed and 1 vs Talon. The four decoys are excluded by their own rule
    /// (same team, other lane, other queue), so none of them lands in a row.
    /// This is the denominator of every play rate over that sample.
    /// </summary>
    private const int TotalSeededGames = 13;

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
    public async Task GetChampionMatchupsAsync_FiltersToRequestedEloBracket()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBracketedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // ALL sums both cohorts: 12 Gold + 12 Iron Yone-vs-Zed games.
        var all = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");
        all!.Matchups.Single(m => m.OpponentChampionId == Opponent).Games.Should().Be(24);

        // A bare Gold filter reads only the Gold-stamped slice of the aggregate.
        var gold = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&eloBracket=GOLD");
        gold!.Matchups.Single(m => m.OpponentChampionId == Opponent).Games
            .Should().Be(12, "only the Gold-stamped games count");

        // GOLD_PLUS unions Gold and above; Iron is below and drops out.
        var goldPlus = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&eloBracket=GOLD_PLUS");
        goldPlus!.Matchups.Single(m => m.OpponentChampionId == Opponent).Games
            .Should().Be(12, "Iron is below Gold and drops out");
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
        // The share is over the champion's whole field, floor or no floor — the one
        // game below the floor is still one game out of every Yone game at the role.
        talon.PlayRate.Should().BeApproximately(1d / TotalSeededGames, 1e-9);
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_AveragesTheGoldGapOverTheLanesItWasMeasuredOn()
    {
        await _fixture.ResetDatabaseAsync();
        // Two patch slices of the same matchup, each with its own gap sample. The
        // measured lanes (20 + 5) are deliberately fewer than the judged ones
        // (30 + 8): the average must divide by the former.
        await SeedGoldGapRowsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var matchups = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");

        var zed = matchups!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.GoldDiffLaneGames.Should().Be(25, "both patch slices' samples fold together");
        zed.AverageGoldDiffAt15.Should().BeApproximately(200d, 1e-9,
            "(6000 - 1000) gold over 25 measured lanes — not over the 38 judged ones");

        // The experience gap folds the same way and lands on the opposite side of
        // zero: gold ahead, XP behind. Neither may be inferred from the other.
        zed.XpDiffLaneGames.Should().Be(25);
        zed.AverageXpDiffAt15.Should().BeApproximately(-100d, 1e-9,
            "(-3000 + 500) xp over the same 25 lanes");
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_ReportsNoGoldGapWhenNoneWasEverMeasured()
    {
        await _fixture.ResetDatabaseAsync();
        // The shape every row folded before #976 has: lane outcomes, no gap. Dividing
        // the empty sum by the judged lanes would print +0 gold — "dead even", the most
        // decisive-looking value the number takes — out of data that does not exist.
        await SeedGoldGapRowsAsync(measured: false);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var matchups = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");

        var zed = matchups!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.AverageGoldDiffAt15.Should().BeNull("no lane of this matchup has a measured gap");
        zed.GoldDiffLaneGames.Should().Be(0);
        zed.AverageXpDiffAt15.Should().BeNull("an unmeasured gap is unknown, never a dead-even lane");
        zed.XpDiffLaneGames.Should().Be(0);
        zed.LaneWinRate.Should().NotBeNull("the outcome counters are unaffected — only the gap is missing");
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_WithOpponent_CarriesLaneDataFromTheAggregate()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedOpponentScopeSampleAsync();
        // The lane counters the fold would have written for that head-to-head.
        await StampLaneCountersAsync(OtherOpponent, laneWins: 9, laneLosses: 3, goldDiffSum: 3300, goldDiffGames: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Both halves come off the same aggregate rows. They used to come from two
        // sources — games from a live self-join over the retention window, lane
        // counters from an aggregate spanning every patch ever folded — which made
        // rows like "13 games, gold gap averaged over 16 lanes" reachable, and made
        // the same matchup answer differently here and on the leaderboard.
        var matchups = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");

        var talon = matchups!.Matchups.Should().ContainSingle().Subject;
        talon.Games.Should().Be(3, "both tracked mains' games are in the aggregate row");
        talon.LaneWinRate.Should().BeApproximately(9d / 12d, 1e-9);
        talon.DecidedLaneGames.Should().Be(12);
        talon.AverageGoldDiffAt15.Should().BeApproximately(275d, 1e-9);
        talon.GoldDiffLaneGames.Should().Be(12);
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_AgreesWithTheLeaderboardOnTheSameMatchup()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var leaderboard = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");
        var searched = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&opponent={Opponent}");

        // The two used to read different sources and disagreed on production by a
        // factor of 1.7 on the same matchup — 22 games listed, 13 searched. Only the
        // floors may differ between them now, never the counts.
        var listed = leaderboard!.Matchups.Single(m => m.OpponentChampionId == Opponent);
        var found = searched!.Matchups.Should().ContainSingle().Subject;
        found.Games.Should().Be(listed.Games);
        found.Wins.Should().Be(listed.Wins);
        // The share too (#1098): the search reads one row, so its denominator comes
        // from a second scoped SUM rather than from the row. It used to report 0 —
        // "a matchup nobody plays", out of the matchup the leaderboard ranks.
        found.PlayRate.Should().BeApproximately(listed.PlayRate, 1e-9);
        found.PlayRate.Should().BeGreaterThan(0d);
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_ExcludesOpponentsBelowTheShareOfGamesFloor()
    {
        await _fixture.ResetDatabaseAsync();
        // 9 980 games against Zed and 20 against Talon. Talon is well clear of the
        // absolute floor of 10 — and is 0.2% of the champion's matchups, under the
        // 0.5% share floor. This is the case an absolute floor cannot express: on a
        // champion this heavily played, ten games is noise, while on a rarely played
        // one the same ten games are the whole matchup.
        await SeedShareFloorRowsAsync(zedGames: 9_980, talonGames: 20);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var matchups = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");

        matchups!.Matchups.Should().OnlyContain(m => m.OpponentChampionId == Opponent);

        var zed = matchups.Matchups.Single();
        zed.PlayRate.Should().BeApproximately(9_980d / 10_000d, 1e-9,
            "the share is measured against the field before the floor drops anything");

        // The search ignores both floors, so the dropped line is still reachable
        // deliberately — the whole point of keeping the lookup floor-free.
        var searched = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");
        searched!.Matchups.Should().ContainSingle().Which.Games.Should().Be(20);
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_BoundsTheWinRateBySampleSize()
    {
        await _fixture.ResetDatabaseAsync();
        // Two matchups a raw win-rate sort ranks the wrong way round: Talon at 60%
        // over 20 games, Zed at 56% over 9 980. Talon's rate is higher and its
        // interval is enormous (±20 points), so its *lower* bound — what the panel
        // orders on — must land below Zed's.
        await SeedShareFloorRowsAsync(zedGames: 9_980, talonGames: 20, zedWins: 5_589, talonWins: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var matchups = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");
        var talon = matchups!.Matchups.Should().ContainSingle().Subject;

        var zedResponse = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&opponent={Opponent}");
        var zed = zedResponse!.Matchups.Should().ContainSingle().Subject;

        talon.WinRate.Should().BeGreaterThan(zed.WinRate, "the raw rates rank Talon first");
        talon.WinRateLowerBound.Should().BeLessThan(zed.WinRateLowerBound,
            "twenty games cannot establish an 80% matchup, and the bound is what says so");
        talon.WinRateLowerBound.Should().BeLessThan(talon.WinRate);
        talon.WinRateUpperBound.Should().BeGreaterThan(talon.WinRate);
        zed.WinRateUpperBound.Should().BeLessThan(talon.WinRateUpperBound,
            "the wide interval is wide at both ends");
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_WithholdsTheLaneRateBelowItsOwnFloor()
    {
        await _fixture.ResetDatabaseAsync();
        // 40 games, of which only 6 lanes were ever decided. The games floors say
        // nothing about that sample: it is roughly half the size on production, and
        // was printing "100% lane" off seven decided lanes — the most confident cell
        // on the panel resting on its smallest sample.
        await SeedThinLaneRowAsync(games: 40, wins: 22, laneWins: 6, laneLosses: 0);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var matchups = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");
        var zed = matchups!.Matchups.Should().ContainSingle().Subject;

        zed.Games.Should().Be(40, "the row itself is well above the games floor");
        zed.LaneWinRate.Should().BeNull("six decided lanes is below MinDecidedLaneGames");
        zed.DecidedLaneGames.Should().Be(6, "the count is still returned, so the caller can say why");
    }

    [Fact]
    public async Task GetPlayerChampionMatchupsAsync_WithOpponent_KeepsLaneDataUnknown()
    {
        await _fixture.ResetDatabaseAsync();
        var nameTag = await SeedOpponentScopeSampleAsync();
        await StampLaneCountersAsync(OtherOpponent, laneWins: 9, laneLosses: 3, goldDiffSum: 3300, goldDiffGames: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // The same aggregate row is right there, and must stay out of a player's row:
        // it is folded over every tracked account, so lending it here would tell this
        // player their lane went the way the population's did.
        var matchups = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/truemains/{nameTag}/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");

        var talon = matchups!.Matchups.Should().ContainSingle().Subject;
        talon.Games.Should().Be(1, "the player scope still narrows to their own game");
        talon.LaneWinRate.Should().BeNull("a population-wide lane is not this player's lane");
        talon.AverageGoldDiffAt15.Should().BeNull();
        talon.GoldDiffLaneGames.Should().Be(0);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task GetChampionMatchupsAsync_ReturnsBadRequestForNonPositiveOpponent(int opponent)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // A champion id is always positive; 0 / negative must be a 400, not a 200
        // with an empty list ([Range] on the opponent query param).
        var response = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}&opponent={opponent}");
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
    public async Task GetPlayerChampionMatchupsAsync_WithOpponent_StaysScopedToThePlayer()
    {
        await _fixture.ResetDatabaseAsync();
        var nameTag = await SeedOpponentScopeSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // The opponent-lookup branch drops the floor to one but must keep the
        // player narrowing: this account owns 1 Yone-vs-Talon game, a second
        // tracked account owns 2, and one game is anonymous. The player slice
        // must see only its own — not 3 — even on a below-global-floor opponent.
        var player = await client.GetAsync(
            $"/truemains/{nameTag}/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");
        player.StatusCode.Should().Be(HttpStatusCode.OK);
        var playerMatchups = await player.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        var talon = playerMatchups!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == OtherOpponent).Subject;
        talon.Games.Should().Be(1, "the opponent lookup stays scoped to this player's games");

        // The same lookup on the global route counts both tracked accounts (1 + 2)
        // and still drops the anonymous game.
        var global = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");
        var globalMatchups = await global.Content.ReadFromJsonAsync<ChampionMatchupsResponse>();
        var globalTalon = globalMatchups!.Matchups.Should().ContainSingle().Subject;
        globalTalon.Games.Should().Be(3, "both tracked accounts count globally; the anonymous game never does");
    }

    [Fact]
    public async Task GetPlayerChampionMatchupsAsync_ReportsTheShareOfThePlayersOwnField()
    {
        await _fixture.ResetDatabaseAsync();
        // The account owns 8 Yone-vs-Zed lane games and 2 Yone-vs-Talon: ten games
        // in its field on this champion and lane, and the denominator of both shares.
        var nameTag = await SeedPlayerFieldSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // The search used to force the total to zero and report a play rate of 0 —
        // "a matchup this player never plays", out of a head-to-head they asked for
        // by name. #1098 fixed exactly this on the aggregate path; the live path kept
        // the hole, and the comment beside it claimed the two agreed.
        var searched = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/truemains/{nameTag}/champions/{Champion}/matchups?position={Position}&opponent={OtherOpponent}");
        var talon = searched!.Matchups.Should().ContainSingle().Subject;
        talon.Games.Should().Be(2);
        talon.PlayRate.Should().BeApproximately(2d / 10d, 1e-9);

        // And the leaderboard's share is over that same field, not over what survived
        // the per-player floor: Talon's two games are below it, and dropping them from
        // the denominator would round Zed's share up to a flat 100%.
        var listed = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/truemains/{nameTag}/champions/{Champion}/matchups?position={Position}");
        var zed = listed!.Matchups.Should().ContainSingle(m => m.OpponentChampionId == Opponent).Subject;
        zed.Games.Should().Be(8);
        zed.PlayRate.Should().BeApproximately(8d / 10d, 1e-9);
        zed.PlayRate.Should().NotBe(1d, "the dropped tail stays in the denominator");
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_OrdersTiedWinRatesByOpponent()
    {
        await _fixture.ResetDatabaseAsync();
        // Two matchups on exactly the same win rate — the common case at these
        // sample sizes, and the one a bare OrderByDescending leaves to whatever
        // order the rows happened to arrive in.
        await SeedShareFloorRowsAsync(zedGames: 20, talonGames: 20, zedWins: 10, talonWins: 10);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var first = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");
        var second = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}");

        // Talon (91) before Zed (238): the opponent id is the tie-breaker, so two
        // identical requests can never hand back the same rows in a different
        // sequence — which a reader can only read as the data having changed.
        first!.Matchups.Select(m => m.OpponentChampionId).Should().Equal(OtherOpponent, Opponent);
        second!.Matchups.Select(m => m.OpponentChampionId)
            .Should().Equal(first.Matchups.Select(m => m.OpponentChampionId));
    }

    [Fact]
    public async Task GetChampionMatchupsAsync_ReturnsBadRequestForAnUnrecognisedEloBracket()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedBracketedMatchupSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // A typo used to resolve to "no restriction", so this answered with both
        // cohorts' 24 games under a Gold label — a rank-scoped number drawn from a
        // population that is not the rank asked for.
        var response = await client.GetAsync(
            $"/champions/{Champion}/matchups?position={Position}&eloBracket=GOLDD");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The recognised filter still reads its own slice, so the rejection is about
        // the value and not about the parameter.
        var gold = await client.GetFromJsonAsync<ChampionMatchupsResponse>(
            $"/champions/{Champion}/matchups?position={Position}&eloBracket=GOLD");
        gold!.Matchups.Single(m => m.OpponentChampionId == Opponent).Games.Should().Be(12);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task GetPlayerChampionMatchupsAsync_ReturnsBadRequestForNonPositiveOpponent(int opponent)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // [Range] runs at model binding, before the account lookup — so a
        // non-positive opponent is a 400 even for an unknown player.
        var response = await client.GetAsync(
            $"/truemains/Nobody-KR1/champions/{Champion}/matchups?position={Position}&opponent={opponent}");
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
        db.MainChampionStats.Add(MainStat(account, Champion, games: 12));

        for (var i = 0; i < 12; i++)
        {
            var patch = i < 10 ? "16.4.521.123" : "16.5.1";
            AddLaneMatchup(db, $"m-zed-{i}", patch, QueueId, yoneWins: i < 7, Opponent, yoneAccountId: account.Id, yonePuuid: account.Puuid);
        }

        // Decoy 1: Zed on the SAME team as Yone (opposite-team rule excludes it).
        var sameTeam = new MatchBuilder().WithId("m-sameteam").WithQueueId(QueueId).WithTimelineIngested().Build();
        db.Matches.Add(sameTeam);
        db.MatchParticipants.Add(Participant(sameTeam.Id, 1, Champion, teamId: 100, Position, win: true, riotAccountId: account.Id, puuid: account.Puuid));
        db.MatchParticipants.Add(Participant(sameTeam.Id, 2, Opponent, teamId: 100, Position, win: true));

        // Decoy 2: Zed present but in a different lane (same-position rule excludes it).
        var otherLane = new MatchBuilder().WithId("m-otherlane").WithQueueId(QueueId).WithTimelineIngested().Build();
        db.Matches.Add(otherLane);
        db.MatchParticipants.Add(Participant(otherLane.Id, 1, Champion, teamId: 100, Position, win: true, riotAccountId: account.Id, puuid: account.Puuid));
        db.MatchParticipants.Add(Participant(otherLane.Id, 2, Opponent, teamId: 200, "TOP", win: true));

        // Decoy 3: a Yone-vs-Zed lane game on a different queue (queue filter excludes it).
        AddLaneMatchup(db, "m-wrongqueue", "16.4.521.123", queueId: 400, yoneWins: true, Opponent, yoneAccountId: account.Id, yonePuuid: account.Puuid);

        // Decoy 4: a single Yone-vs-Talon lane game (different opponent; also
        // below the floor on its own, used by the not-enough-data test).
        AddLaneMatchup(db, "m-talon", "16.4.521.123", QueueId, yoneWins: true, OtherOpponent, yoneAccountId: account.Id, yonePuuid: account.Puuid);

        await db.SaveChangesAsync();
        await RunAggregationAsync();
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
        db.MainChampionStats.Add(MainStat(account, Champion, games: 11));
        db.MainChampionStats.Add(MainStat(otherAccount, Champion, games: 5));

        for (var i = 0; i < 11; i++)
        {
            AddLaneMatchup(db, $"ms-owned-{i}", "16.4.521.123", QueueId, yoneWins: i < 6, Opponent,
                yoneAccountId: account.Id, yonePuuid: account.Puuid);
        }

        // A second tracked account's games: part of the global pool, never this
        // player's slice.
        for (var i = 0; i < 5; i++)
        {
            AddLaneMatchup(db, $"ms-other-{i}", "16.4.521.123", QueueId, yoneWins: true, Opponent,
                yoneAccountId: otherAccount.Id, yonePuuid: otherAccount.Puuid);
        }

        // Anonymous games: counted by neither scope (no tracked account).
        for (var i = 0; i < 3; i++)
        {
            AddLaneMatchup(db, $"ms-anon-{i}", "16.4.521.123", QueueId, yoneWins: true, Opponent);
        }

        await db.SaveChangesAsync();
        await RunAggregationAsync();
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
        db.MainChampionStats.Add(MainStat(account, Champion, games: 5));

        for (var i = 0; i < 5; i++)
        {
            AddLaneMatchup(db, $"mf-zed-{i}", "16.4.521.123", QueueId, yoneWins: i < 3, Opponent,
                yoneAccountId: account.Id, yonePuuid: account.Puuid);
        }

        await db.SaveChangesAsync();
        await RunAggregationAsync();
        return $"{account.GameName}-{account.TagLine}";
    }

    /// <summary>
    /// Seeds one tracked account with a two-opponent field on the same champion and
    /// lane: 8 Yone-vs-Zed lane games (5 won) and 2 Yone-vs-Talon (1 won). Ten games
    /// in total, of which only the Zed line clears the per-player floor — so the
    /// denominator of a play rate and the rows that survive the floor are two
    /// different sets, which is the whole point of the fixture. Returns the tag.
    /// </summary>
    private async Task<string> SeedPlayerFieldSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("MatchupField")
            .WithTagLine("KR1")
            .WithPuuid("matchup-field-puuid")
            .Build();
        db.RiotAccounts.Add(account);
        db.MainChampionStats.Add(MainStat(account, Champion, games: 10));

        for (var i = 0; i < 8; i++)
        {
            AddLaneMatchup(db, $"mfd-zed-{i}", "16.4.521.123", QueueId, yoneWins: i < 5, Opponent,
                yoneAccountId: account.Id, yonePuuid: account.Puuid);
        }

        for (var i = 0; i < 2; i++)
        {
            AddLaneMatchup(db, $"mfd-talon-{i}", "16.4.521.123", QueueId, yoneWins: i < 1, OtherOpponent,
                yoneAccountId: account.Id, yonePuuid: account.Puuid);
        }

        await db.SaveChangesAsync();
        await RunAggregationAsync();
        return $"{account.GameName}-{account.TagLine}";
    }

    /// <summary>
    /// Seeds one below-global-floor opponent (Talon) split across scopes so the
    /// player-scoped <c>?opponent=</c> path can be checked: the account under test
    /// owns 1 Yone-vs-Talon game, a second tracked account owns 2, and one is
    /// anonymous. The player slice must see 1, the global pool 3. Returns the tag.
    /// </summary>
    private async Task<string> SeedOpponentScopeSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("MatchupOppMain")
            .WithTagLine("KR1")
            .WithPuuid("matchup-oppmain-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        var otherAccount = new RiotAccountBuilder()
            .WithGameName("MatchupOppOther")
            .WithTagLine("KR1")
            .WithPuuid("matchup-oppother-puuid")
            .Build();
        db.RiotAccounts.Add(otherAccount);
        db.MainChampionStats.Add(MainStat(account, Champion, games: 1));
        db.MainChampionStats.Add(MainStat(otherAccount, Champion, games: 2));

        AddLaneMatchup(db, "mo-owned-0", "16.4.521.123", QueueId, yoneWins: true, OtherOpponent,
            yoneAccountId: account.Id, yonePuuid: account.Puuid);
        for (var i = 0; i < 2; i++)
        {
            AddLaneMatchup(db, $"mo-other-{i}", "16.4.521.123", QueueId, yoneWins: true, OtherOpponent,
                yoneAccountId: otherAccount.Id, yonePuuid: otherAccount.Puuid);
        }
        AddLaneMatchup(db, "mo-anon-0", "16.4.521.123", QueueId, yoneWins: true, OtherOpponent);

        await db.SaveChangesAsync();
        await RunAggregationAsync();
        return $"{account.GameName}-{account.TagLine}";
    }

    /// <summary>
    /// Seeds two <c>champion_matchup_stats</c> rows directly — Gold (12 games, 8
    /// wins) and Iron (12 games, 6 wins) — for the same champion/position/opponent/
    /// patch. Written straight to the aggregate table (bypassing the ingestor
    /// pipeline) so this exercises only the read-side band filter/fold, which is
    /// what the elo-bracket query parameter controls.
    /// </summary>
    private async Task SeedBracketedMatchupSampleAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var now = DateTime.UtcNow;
        db.ChampionMatchupStats.AddRange(
            new ChampionMatchupStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                OpponentChampionId = Opponent,
                Patch = "16.4",
                EloBracket = EloBracket.Gold,
                Games = 12,
                Wins = 8,
                AggregatedAtUtc = now
            },
            new ChampionMatchupStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                OpponentChampionId = Opponent,
                Patch = "16.4",
                EloBracket = EloBracket.Iron,
                Games = 12,
                Wins = 6,
                AggregatedAtUtc = now
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds two aggregate rows of the same matchup on different patches, carrying
    /// lane outcomes and — when <paramref name="measured"/> — the gold gap behind them
    /// (#976). Written straight to the table so only the read-side fold is exercised.
    ///
    /// <para>
    /// The gap's sample is deliberately smaller than <c>LaneGames</c> on both rows: it
    /// is what a partially-drained fold leaves behind, and the average must never quietly
    /// borrow the larger denominator.
    /// </para>
    /// </summary>
    private async Task SeedGoldGapRowsAsync(bool measured = true)
    {
        await using var db = _fixture.CreateDbContext();

        var now = DateTime.UtcNow;
        db.ChampionMatchupStats.AddRange(
            new ChampionMatchupStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                OpponentChampionId = Opponent,
                Patch = "16.4",
                EloBracket = EloBracket.Gold,
                Games = 30,
                Wins = 18,
                LaneGames = 30,
                LaneWins = 14,
                LaneLosses = 6,
                LaneGoldDiffSum = measured ? 6000 : 0,
                LaneGoldDiffGames = measured ? 20 : 0,
                // Pointing the other way from the gold on this slice, which is the
                // reading the two counters exist to keep separable (#1111).
                LaneXpDiffSum = measured ? -3000 : 0,
                LaneXpDiffGames = measured ? 20 : 0,
                AggregatedAtUtc = now
            },
            new ChampionMatchupStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                OpponentChampionId = Opponent,
                Patch = "16.5",
                EloBracket = EloBracket.Gold,
                Games = 8,
                Wins = 3,
                LaneGames = 8,
                LaneWins = 2,
                LaneLosses = 4,
                LaneGoldDiffSum = measured ? -1000 : 0,
                LaneGoldDiffGames = measured ? 5 : 0,
                LaneXpDiffSum = measured ? 500 : 0,
                LaneXpDiffGames = measured ? 5 : 0,
                AggregatedAtUtc = now
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds two aggregate rows sized so the <em>share</em> floor is the binding one:
    /// a dominant opponent and a thin one that clears the absolute floor comfortably.
    /// Written straight to the table — the ratio needed to make the share floor bite
    /// is ten thousand games, which no seeder should fold match by match.
    /// </summary>
    private async Task SeedShareFloorRowsAsync(
        int zedGames, int talonGames, int? zedWins = null, int? talonWins = null)
    {
        await using var db = _fixture.CreateDbContext();

        var now = DateTime.UtcNow;
        db.ChampionMatchupStats.AddRange(
            new ChampionMatchupStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                OpponentChampionId = Opponent,
                Patch = "16.4",
                EloBracket = EloBracket.Gold,
                Games = zedGames,
                Wins = zedWins ?? zedGames / 2,
                AggregatedAtUtc = now,
            },
            new ChampionMatchupStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                OpponentChampionId = OtherOpponent,
                Patch = "16.4",
                EloBracket = EloBracket.Gold,
                Games = talonGames,
                Wins = talonWins ?? talonGames / 2,
                AggregatedAtUtc = now,
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds one aggregate row whose games clear every games floor while its decided
    /// lanes do not clear theirs — the shape that made the lane column the least
    /// trustworthy number on the panel.
    /// </summary>
    private async Task SeedThinLaneRowAsync(int games, int wins, int laneWins, int laneLosses)
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionMatchupStats.Add(new ChampionMatchupStat
        {
            ChampionId = Champion,
            TeamPosition = Position,
            OpponentChampionId = Opponent,
            Patch = "16.4",
            EloBracket = EloBracket.Gold,
            Games = games,
            Wins = wins,
            LaneGames = laneWins + laneLosses,
            LaneWins = laneWins,
            LaneLosses = laneLosses,
            LaneGoldDiffSum = 1200,
            LaneGoldDiffGames = laneWins + laneLosses,
            AggregatedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Stamps lane counters onto the aggregate row the matchup fold already wrote for
    /// an opponent, so the read side has the lane half of that head-to-head.
    /// </summary>
    private async Task StampLaneCountersAsync(
        int opponentChampionId, int laneWins, int laneLosses, long goldDiffSum, int goldDiffGames)
    {
        await using var db = _fixture.CreateDbContext();

        var row = await db.ChampionMatchupStats.SingleAsync(s =>
            s.ChampionId == Champion
            && s.TeamPosition == Position
            && s.OpponentChampionId == opponentChampionId);

        row.LaneGames = laneWins + laneLosses;
        row.LaneWins = laneWins;
        row.LaneLosses = laneLosses;
        row.LaneGoldDiffSum = goldDiffSum;
        row.LaneGoldDiffGames = goldDiffGames;

        await db.SaveChangesAsync();
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
        Guid? yoneAccountId = null,
        string eloBracket = "",
        string? yonePuuid = null)
    {
        var match = new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(queueId)
            .WithGameVersion(gameVersion)
            .WithTimelineIngested()
            .Build();
        db.Matches.Add(match);

        db.MatchParticipants.Add(Participant(
            match.Id, 1, Champion, teamId: 100, Position, win: yoneWins, riotAccountId: yoneAccountId,
            eloBracket: eloBracket, puuid: yonePuuid));
        db.MatchParticipants.Add(Participant(
            match.Id, 2, opponentChampionId, teamId: 200, Position, win: !yoneWins));
    }

    /// <summary>
    /// The fold's champion-side cohort is "a main of this champion"
    /// (<c>Data.Aggregation.MatchupCohort</c>), so every account whose games are
    /// meant to reach the aggregate needs one of these rows — including the second
    /// tracked account in the seeds that assert what the *global* pool holds.
    /// </summary>
    private static MainChampionStat MainStat(RiotAccount account, int championId, int games)
        => new()
        {
            PlatformId = account.PlatformId,
            Puuid = account.Puuid,
            ChampionId = championId,
            TotalMatches = games,
            ChampionMatches = games,
            PlayRate = 1.0,
            IsMain = true,
            PrimaryPosition = Position,
            CalculatedAtUtc = DateTime.UtcNow
        };

    private static MatchParticipant Participant(
        string matchId,
        int participantId,
        int championId,
        int teamId,
        string teamPosition,
        bool win,
        Guid? riotAccountId = null,
        string eloBracket = "",
        string? puuid = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            // The champion-side row must carry its account's real puuid: the fold's
            // cohort joins main_champion_stats on (platform, puuid, champion), the
            // way RiotMatchMapper writes them. A synthetic puuid here would make
            // every seeded game fold to nothing.
            Puuid = puuid ?? $"puuid-{matchId}-{participantId}",
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
            EloBracket = eloBracket,
            ItemEvents = [],
            SkillEvents = []
        };

    /// <summary>
    /// Runs the ingestor aggregation against the seeded raw rows so every global
    /// matchups slice — leaderboard and single-opponent search alike — has data.
    /// Only the player-scoped slice stays live, so this is a no-op for it, but
    /// running it after every seed keeps the full pipeline under test.
    /// </summary>
    private async Task RunAggregationAsync()
    {
        var process = new ChampionMatchupLeadAggregationProcess(
            NullLogger<ChampionMatchupLeadAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new MatchupLeadAggregationOptions()),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);
        await process.RunCoreAsync(CancellationToken.None);
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
                new KeyValuePair<string, string?>("ChampionsList:MinMatchupGames", "10"),
                new KeyValuePair<string, string?>("ChampionsList:MinMatchupPlayRate", "0.005"),
                new KeyValuePair<string, string?>("ChampionsList:MinDecidedLaneGames", "10"),
                new KeyValuePair<string, string?>("ChampionsList:MinPlayerMatchupGames", "3"),
            ]);
}
