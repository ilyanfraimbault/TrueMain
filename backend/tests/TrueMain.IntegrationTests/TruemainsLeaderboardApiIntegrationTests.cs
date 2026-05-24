using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

public sealed class TruemainsLeaderboardApiIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TruemainsLeaderboardApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_orders_by_rank_score_LP_aware_and_skips_unranked()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Mix of ranks across two exposed regions. Master/GM/Challenger share
        // an apex super-tier in the score formula and are departed by raw LP
        // — matching how u.gg / op.gg ladders display them. So the order is
        // Master 2625 > Challenger 800 > Master 20 > Diamond I 90 > Diamond II 50.
        // The Diamond rows still sit below every apex row (tier weight wins
        // when LP at apex is low) and the unranked account never appears.
        await using (var db = _fixture.CreateDbContext())
        {
            var masterHigh = Account("master-high", "MasterHigh", "EUW1");
            var masterLow = Account("master-low", "MasterLow", "EUW1");
            var challenger = Account("challenger", "Chall", "NA1");
            var diamondOne = Account("diamond-one", "DiamondOne", "EUW1");
            var diamondTwo = Account("diamond-two", "DiamondTwo", "EUW1");
            var unranked = Account("unranked", "Unranked", "EUW1");

            db.RiotAccounts.AddRange(masterHigh, masterLow, challenger, diamondOne, diamondTwo, unranked);
            db.RankSnapshots.AddRange(
                Snapshot(masterHigh, "MASTER", "I", 2625, now),
                Snapshot(masterLow, "MASTER", "I", 20, now),
                Snapshot(challenger, "CHALLENGER", "I", 800, now),
                Snapshot(diamondOne, "DIAMOND", "I", 90, now),
                Snapshot(diamondTwo, "DIAMOND", "II", 50, now));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var leaderboard = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        leaderboard.Should().NotBeNull();

        leaderboard!.Total.Should().Be(5, "unranked accounts are excluded from V1");
        leaderboard.Rows.Should().HaveCount(5);

        leaderboard.Rows.Select(r => r.Identity.GameName)
            .Should().ContainInOrder("MasterHigh", "Chall", "MasterLow", "DiamondOne", "DiamondTwo");

        // Ranks are server-computed 1-indexed positions over the filtered set.
        leaderboard.Rows.Select(r => r.Rank).Should().ContainInOrder(1, 2, 3, 4, 5);

        // Verify the score reflects the formula and that the top-LP master
        // ends up with the highest score.
        leaderboard.Rows[0].Ranked!.Score.Should().BeGreaterThan(leaderboard.Rows[2].Ranked!.Score);
    }

    [Fact]
    public async Task List_filters_by_region_and_maps_platform_to_region_slug()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var euw = Account("euw-1", "Euw1Player", "EUW1");
            var eun = Account("eun-1", "Eun1Player", "EUN1");
            var na = Account("na-1", "Na1Player", "NA1");
            var kr = Account("kr-1", "KrPlayer", "KR");
            var jp = Account("jp-1", "JpPlayer", "JP1");

            db.RiotAccounts.AddRange(euw, eun, na, kr, jp);
            db.RankSnapshots.AddRange(
                Snapshot(euw, "DIAMOND", "II", 50, now),
                Snapshot(eun, "GOLD", "I", 30, now),
                Snapshot(na, "PLATINUM", "III", 10, now),
                Snapshot(kr, "CHALLENGER", "I", 1200, now),
                Snapshot(jp, "MASTER", "I", 200, now));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // No filter → JP1 row is excluded (Korea pill is KR-only in V1).
        var all = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        all!.Total.Should().Be(4);
        all.Rows.Should().NotContain(r => r.Identity.PlatformId == "JP1");

        // Region=europe → EUW1 + EUN1.
        var europe = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?region=europe");
        europe!.Total.Should().Be(2);
        europe.Rows.Select(r => r.Identity.PlatformId)
            .Should().BeEquivalentTo(["EUW1", "EUN1"]);
        europe.Rows.Should().OnlyContain(r => r.Region == "europe");

        // Region=korea → KR only (not JP1).
        var korea = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?region=korea");
        korea!.Total.Should().Be(1);
        korea.Rows[0].Identity.PlatformId.Should().Be("KR");
        korea.Rows[0].Region.Should().Be("korea");

        // Region=americas → NA1.
        var americas = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?region=americas");
        americas!.Total.Should().Be(1);
        americas.Rows[0].Identity.PlatformId.Should().Be("NA1");
        americas.Rows[0].Region.Should().Be("americas");
    }

    [Fact]
    public async Task List_filters_by_position_and_champion_via_main_champion_stats()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var midYasuo = Account("mid-yasuo", "MidYasuo", "EUW1");
            var topGaren = Account("top-garen", "TopGaren", "EUW1");
            var midAhri = Account("mid-ahri", "MidAhri", "EUW1");

            db.RiotAccounts.AddRange(midYasuo, topGaren, midAhri);
            db.RankSnapshots.AddRange(
                Snapshot(midYasuo, "DIAMOND", "I", 50, now),
                Snapshot(topGaren, "DIAMOND", "I", 50, now),
                Snapshot(midAhri, "DIAMOND", "I", 50, now));

            // Yasuo (157) mid main, Garen (86) top main, Ahri (103) mid main.
            db.MainChampionStats.AddRange(
                MainStat("mid-yasuo", "EUW1", 157, "MIDDLE", isMain: true),
                MainStat("top-garen", "EUW1", 86, "TOP", isMain: true),
                MainStat("mid-ahri", "EUW1", 103, "MIDDLE", isMain: true));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Position=MIDDLE → Yasuo + Ahri.
        var middles = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?position=MIDDLE");
        middles!.Total.Should().Be(2);
        middles.Rows.Select(r => r.Identity.GameName)
            .Should().BeEquivalentTo(["MidYasuo", "MidAhri"]);

        // championId=157 → only Yasuo main.
        var yasuoMains = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?championId=157");
        yasuoMains!.Total.Should().Be(1);
        yasuoMains.Rows[0].Identity.GameName.Should().Be("MidYasuo");

        // Position=TOP & championId=86 → only Garen.
        var topGarenOnly = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?position=TOP&championId=86");
        topGarenOnly!.Total.Should().Be(1);
        topGarenOnly.Rows[0].Identity.GameName.Should().Be("TopGaren");

        // Mismatched combination (Yasuo champion with TOP position) → empty.
        var noMatch = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?position=TOP&championId=157");
        noMatch!.Total.Should().Be(0);
        noMatch.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task List_paginates_with_server_computed_rank()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            for (var i = 0; i < 7; i++)
            {
                var id = $"player-{i}";
                var account = Account(id, $"Player{i}", "EUW1");
                db.RiotAccounts.Add(account);
                db.RankSnapshots.Add(Snapshot(account, "DIAMOND", "I", 99 - i, now));
            }

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var page1 = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?pageSize=3&page=1");
        page1!.Total.Should().Be(7);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(3);
        page1.Rows.Should().HaveCount(3);
        page1.Rows.Select(r => r.Rank).Should().ContainInOrder(1, 2, 3);

        var page2 = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?pageSize=3&page=2");
        page2!.Page.Should().Be(2);
        page2.Rows.Select(r => r.Rank).Should().ContainInOrder(4, 5, 6);

        var page3 = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?pageSize=3&page=3");
        page3!.Rows.Should().HaveCount(1);
        page3.Rows[0].Rank.Should().Be(7);

        // Past-the-end page returns empty rows + real total so the UI's
        // pagination control still resolves to a valid range.
        var page4 = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?pageSize=3&page=4");
        page4!.Total.Should().Be(7);
        page4.Rows.Should().BeEmpty();
    }

    private static RiotAccount Account(string puuid, string gameName, string platformId, string? tagLine = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine ?? platformId,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private static RankSnapshot Snapshot(RiotAccount account, string tier, string division, int leaguePoints, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            // Setting the nav property lets EF resolve the FK on SaveChanges
            // — no need to flush the account first, which keeps the seed
            // arrange phase to a single round trip.
            RiotAccount = account,
            CapturedAtUtc = now,
            Tier = tier,
            Division = division,
            LeaguePoints = leaguePoints,
            Wins = 50,
            Losses = 50,
        };

    private static MainChampionStat MainStat(string puuid, string platformId, int championId, string primaryPosition, bool isMain)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 100,
            ChampionMatches = 50,
            PlayRate = 0.5d,
            IsMain = isMain,
            IsOtp = false,
            PrimaryPosition = primaryPosition,
            PositionBreakdown = [new PositionStat { Position = primaryPosition, Games = 50, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow,
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("ConnectionStrings:TrueMain", fixture.ConnectionString),
                    new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                    new KeyValuePair<string, string?>("Ops:ApiKey", "integration-tests-ops-key-0123456789-padding"),
                ]);
            });
        }
    }
}
