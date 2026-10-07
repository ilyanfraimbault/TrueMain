using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The Games / KDA / WR orders of <c>GET /truemains</c> (#1737): each ranks the
/// whole eligible board on the figure the row prints, pages it through the API,
/// sinks a row without that figure to the bottom and breaks ties on the ranked
/// standing.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TruemainsLeaderboardStatSortApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public TruemainsLeaderboardStatSortApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Ranked standing: Apex (Challenger) > Master > Diamond > Fresh.
    //
    //            games   KDA                    WR (snapshot)
    // Apex       25      (25+25)/25 = 2.0       50/50 = 0.5
    // Master     25      (20+20)/10 = 4.0       70/30 = 0.7
    // Diamond    40      (40+40)/5  = 16.0      30/70 = 0.3
    // Fresh      —       —                      — (no W-L on the snapshot)
    //
    // Apex and Master tie on games, so the games order also proves the
    // ranked-standing tiebreak; Fresh has no aggregate and no W-L, so it proves
    // a missing figure sinks rather than ranking as a zero.
    [Theory]
    [InlineData("games", new[] { "Diamond", "Apex", "Master", "Fresh" })]
    [InlineData("kda", new[] { "Diamond", "Master", "Apex", "Fresh" })]
    [InlineData("winRate", new[] { "Master", "Apex", "Diamond", "Fresh" })]
    [InlineData("WINRATE", new[] { "Master", "Apex", "Diamond", "Fresh" })]
    public async Task StatSortOrdersTheWholeBoard(string sort, string[] expectedOrder)
    {
        await SeedBoardAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var board = await client.GetFromJsonAsync<LeaderboardResponse>($"/truemains?sort={sort}");

        board!.Total.Should().Be(4);
        board.Rows.Select(row => row.Identity.GameName).Should().Equal(expectedOrder);
        board.Rows.Select(row => row.Rank).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task StatSortPagesTheServerOrderRatherThanOnePage()
    {
        await SeedBoardAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var firstPage = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?sort=kda&pageSize=2&page=1");
        var secondPage = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?sort=kda&pageSize=2&page=2");

        firstPage!.Total.Should().Be(4);
        firstPage.Rows.Select(row => row.Identity.GameName).Should().Equal("Diamond", "Master");
        secondPage!.Total.Should().Be(4);
        secondPage.Rows.Select(row => row.Identity.GameName).Should().Equal("Apex", "Fresh");
        secondPage.Rows.Select(row => row.Rank).Should().Equal(3, 4);

        // The figure the order ranked on is the one the row prints.
        firstPage.Rows[0].Stats.Kda.Should().BeApproximately(16d, 1e-9);
        secondPage.Rows[1].Stats.Kda.Should().BeNull();
    }

    [Fact]
    public async Task StatSortRespectsTheFilters()
    {
        await SeedBoardAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Diamond is the only NA1 account: the region filter must narrow the
        // stat ranking (and its total) exactly as it narrows the default one.
        var americas = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?sort=games&region=americas");

        americas!.Total.Should().Be(1);
        americas.Rows.Should().ContainSingle().Which.Identity.GameName.Should().Be("Diamond");
    }

    private async Task SeedBoardAsync()
    {
        var now = DateTime.UtcNow;

        var apex = Account("apex-puuid", "Apex", "EUW1");
        var master = Account("master-puuid", "Master", "EUW1");
        var diamond = Account("diamond-puuid", "Diamond", "NA1");
        var fresh = Account("fresh-puuid", "Fresh", "EUW1");

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.AddRange(apex, master, diamond, fresh);
        db.RankSnapshots.AddRange(
            Snapshot(apex, "CHALLENGER", "I", 900, now, wins: 50, losses: 50),
            Snapshot(master, "MASTER", "I", 100, now, wins: 70, losses: 30),
            Snapshot(diamond, "DIAMOND", "I", 50, now, wins: 30, losses: 70),
            Snapshot(fresh, "DIAMOND", "IV", 0, now, wins: null, losses: null));
        db.MainChampionStats.AddRange(
            MainStat(apex, championId: 1),
            MainStat(master, championId: 2),
            MainStat(diamond, championId: 3),
            MainStat(fresh, championId: 4));
        db.ChampionAggregateScopes.AddRange(
            Scope(apex, championId: 1, games: 25, kills: 25, deaths: 25, assists: 25, now),
            // Two patches for Master: the ranking must sum them like the row does.
            Scope(master, championId: 2, games: 15, kills: 12, deaths: 6, assists: 12, now, patch: "16.5"),
            Scope(master, championId: 2, games: 10, kills: 8, deaths: 4, assists: 8, now, patch: "16.4"),
            Scope(diamond, championId: 3, games: 40, kills: 40, deaths: 5, assists: 40, now),
            // A non-main scope must not count, exactly as on the row (#1346).
            Scope(fresh, championId: 4, games: 99, kills: 99, deaths: 1, assists: 99, now, isMain: false));
        await db.SaveChangesAsync();
    }

    private static RiotAccount Account(string puuid, string gameName, string platformId)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = platformId,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private static RankSnapshot Snapshot(
        RiotAccount account,
        string tier,
        string division,
        int leaguePoints,
        DateTime now,
        int? wins,
        int? losses)
    {
        // Mirror the ingestion writer: the denormalised Score is the tiebreak.
        account.Score = RankScore.Compute(tier, division, leaguePoints);
        return new RankSnapshot
        {
            Id = Guid.NewGuid(),
            RiotAccount = account,
            CapturedAtUtc = now,
            Tier = tier,
            Division = division,
            LeaguePoints = leaguePoints,
            Wins = wins,
            Losses = losses,
        };
    }

    private static MainChampionStat MainStat(RiotAccount account, int championId)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = account.PlatformId,
            Puuid = account.Puuid,
            ChampionId = championId,
            TotalMatches = 50,
            ChampionMatches = 50,
            PlayRate = 1d,
            IsMain = true,
            IsOtp = true,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 50, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow,
        };

    private static ChampionAggregateScope Scope(
        RiotAccount account,
        int championId,
        int games,
        int kills,
        int deaths,
        int assists,
        DateTime now,
        string patch = "16.5",
        bool isMain = true)
        => new()
        {
            Id = Guid.NewGuid(),
            RiotAccountId = account.Id,
            ChampionId = championId,
            GameVersion = patch,
            PlatformId = account.PlatformId,
            QueueId = 420,
            Position = "MIDDLE",
            EloBracket = EloBracket.Diamond,
            IsMain = isMain,
            Games = games,
            Wins = games / 2,
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
            LastGameStartTimeUtc = now,
            AggregatedAtUtc = now,
        };

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                // These seeds carry no participants, so the ranked-games floor is
                // disabled — it is covered by the leaderboard's own suite.
                new KeyValuePair<string, string?>("TruemainsLeaderboard:MinRankedGames", "0"),
            ]);
}
