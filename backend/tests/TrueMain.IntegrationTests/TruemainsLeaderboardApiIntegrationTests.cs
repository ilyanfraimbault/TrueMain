using System.Net;
using System.Net.Http.Json;
using Core.Lol.Ranking;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class TruemainsLeaderboardApiIntegrationTests
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
        //
        // All ranked accounts also need an IsMain=true row to clear the
        // strict truemain filter (issue #184).
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
            db.MainChampionStats.AddRange(
                MainStat("master-high", "EUW1", 1, "MIDDLE", isMain: true),
                MainStat("master-low", "EUW1", 1, "MIDDLE", isMain: true),
                MainStat("challenger", "NA1", 1, "MIDDLE", isMain: true),
                MainStat("diamond-one", "EUW1", 1, "MIDDLE", isMain: true),
                MainStat("diamond-two", "EUW1", 1, "MIDDLE", isMain: true));

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
            // Strict truemain filter (issue #184) requires every visible row
            // to back an IsMain=true main_champion_stats entry. JP1 doesn't
            // need one — it's excluded by the region filter anyway.
            db.MainChampionStats.AddRange(
                MainStat("euw-1", "EUW1", 1, "MIDDLE", isMain: true),
                MainStat("eun-1", "EUN1", 1, "MIDDLE", isMain: true),
                MainStat("na-1", "NA1", 1, "MIDDLE", isMain: true),
                MainStat("kr-1", "KR", 1, "MIDDLE", isMain: true));

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
    public async Task List_position_filter_uses_position_breakdown_share_threshold()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Three Yasuo mains with different lane splits. The filter should
        // include any player who plays the queried position at least the
        // configured share of the time with a main champion (20%), not only
        // the player whose top lane is the queried position.
        await using (var db = _fixture.CreateDbContext())
        {
            var flexTop = Account("flex-top", "FlexTop", "EUW1");
            var cameoTop = Account("cameo-top", "CameoTop", "EUW1");
            var pureTop = Account("pure-top", "PureTop", "EUW1");

            db.RiotAccounts.AddRange(flexTop, cameoTop, pureTop);
            db.RankSnapshots.AddRange(
                Snapshot(flexTop, "DIAMOND", "I", 50, now),
                Snapshot(cameoTop, "DIAMOND", "I", 50, now),
                Snapshot(pureTop, "DIAMOND", "I", 50, now));

            // FlexTop: MIDDLE 80% / TOP 20% — TOP just clears the bar.
            db.MainChampionStats.Add(MainStatWithBreakdown(
                "flex-top", "EUW1", championId: 157, primaryPosition: "MIDDLE",
                breakdown:
                [
                    new PositionStat { Position = "MIDDLE", Games = 80, Rate = 0.8d },
                    new PositionStat { Position = "TOP", Games = 20, Rate = 0.2d },
                ]));

            // CameoTop: MIDDLE 85% / TOP 15% — TOP cameo is filtered out.
            db.MainChampionStats.Add(MainStatWithBreakdown(
                "cameo-top", "EUW1", championId: 157, primaryPosition: "MIDDLE",
                breakdown:
                [
                    new PositionStat { Position = "MIDDLE", Games = 85, Rate = 0.85d },
                    new PositionStat { Position = "TOP", Games = 15, Rate = 0.15d },
                ]));

            // PureTop: TOP 100% — control row, should appear under TOP only.
            db.MainChampionStats.Add(MainStatWithBreakdown(
                "pure-top", "EUW1", championId: 86, primaryPosition: "TOP",
                breakdown:
                [
                    new PositionStat { Position = "TOP", Games = 50, Rate = 1d },
                ]));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var middles = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?position=MIDDLE");
        middles!.Total.Should().Be(2);
        middles.Rows.Select(r => r.Identity.GameName)
            .Should().BeEquivalentTo(["FlexTop", "CameoTop"]);

        // FlexTop (20% top) qualifies; CameoTop (15% top) does not.
        var tops = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?position=TOP");
        tops!.Total.Should().Be(2);
        tops.Rows.Select(r => r.Identity.GameName)
            .Should().BeEquivalentTo(["FlexTop", "PureTop"]);
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
                // Strict truemain filter (issue #184): pagination is over the
                // truemains subset, so every paginated row needs an
                // IsMain=true main_champion_stats entry.
                db.MainChampionStats.Add(MainStat(id, "EUW1", 1, "MIDDLE", isMain: true));
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

    [Fact]
    public async Task List_filters_out_accounts_below_min_ranked_games_threshold()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Three accounts at the same rank, with 1 / 4 / 6 ranked games. The
        // dedicated factory below sets MinRankedGames=5 so only the 6-game
        // account should appear.
        await using (var db = _fixture.CreateDbContext())
        {
            var oneGame = Account("one", "OneGame", "EUW1");
            var fourGames = Account("four", "FourGames", "EUW1");
            var sixGames = Account("six", "SixGames", "EUW1");

            db.RiotAccounts.AddRange(oneGame, fourGames, sixGames);
            db.RankSnapshots.AddRange(
                Snapshot(oneGame, "DIAMOND", "I", 50, now),
                Snapshot(fourGames, "DIAMOND", "I", 50, now),
                Snapshot(sixGames, "DIAMOND", "I", 50, now));
            // Strict truemain filter (issue #184): the min-games floor runs
            // alongside the IsMain=true requirement inside the same EXISTS, so
            // all three candidates need a main_champion_stats row. The floor is
            // measured on main_champion_stats.TotalMatches (the ranked-solo
            // analysis-window count), not a match_participants COUNT — see
            // TruemainsLeaderboardQueryService.CountAsync. Only "six" (6 >= 5)
            // clears the MinRankedGames=5 cut-off below.
            db.MainChampionStats.AddRange(
                MainStat("one", "EUW1", 1, "MIDDLE", isMain: true, totalMatches: 1),
                MainStat("four", "EUW1", 1, "MIDDLE", isMain: true, totalMatches: 4),
                MainStat("six", "EUW1", 1, "MIDDLE", isMain: true, totalMatches: 6));

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactoryWithMinGames(_fixture, minRankedGames: 5);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        response!.Total.Should().Be(1, "only the account with >= 5 games should appear");
        response.Rows.Single().Identity.GameName.Should().Be("SixGames");
    }

    [Fact]
    public async Task List_excludes_accounts_without_truemain_main_champion_stat()
    {
        // The /truemains page is, by definition, the list of truemains: an
        // account that has rank data but no IsMain=true row in
        // main_champion_stats (fresh ingest, or main analysis hasn't run yet,
        // or the player isn't a true main of anyone) must NOT appear on the
        // leaderboard. See issue #184.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var realMain = Account("real-main", "RealMain", "EUW1");
            var notMain = Account("not-main", "NotMain", "EUW1");
            var freshIngest = Account("fresh", "FreshIngest", "EUW1");

            db.RiotAccounts.AddRange(realMain, notMain, freshIngest);
            db.RankSnapshots.AddRange(
                Snapshot(realMain, "DIAMOND", "I", 80, now),
                Snapshot(notMain, "DIAMOND", "I", 80, now),
                Snapshot(freshIngest, "DIAMOND", "I", 80, now));

            // Only `real-main` qualifies. `not-main` has a row but with
            // IsMain=false (the analyzer ran and decided the player isn't a
            // main of any champion). `fresh-ingest` has no row at all
            // (analyzer hasn't seen the puuid yet).
            db.MainChampionStats.AddRange(
                MainStat("real-main", "EUW1", 157, "MIDDLE", isMain: true),
                MainStat("not-main", "EUW1", 86, "TOP", isMain: false));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        response!.Total.Should().Be(1);
        response.Rows.Single().Identity.GameName.Should().Be("RealMain");
    }

    [Fact]
    public async Task List_caches_response_for_identical_request_shape()
    {
        // Proves the 30s response cache short-circuits the four SQL queries
        // for the same (page, filters) shape. We can't easily wait the TTL
        // out inside a test, so we use the inverse signal: mutate the DB
        // *after* the first request and verify the second request still sees
        // the snapshot from the first — which only happens if the second one
        // came back from the cache without hitting the DB.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var first = Account("first", "First", "EUW1");
            db.RiotAccounts.Add(first);
            db.RankSnapshots.Add(Snapshot(first, "DIAMOND", "I", 80, now));
            db.MainChampionStats.Add(MainStat("first", "EUW1", 157, "MIDDLE", isMain: true));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // First request — populates the cache for this (platforms, …) shape.
        var firstResponse = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        firstResponse!.Total.Should().Be(1);
        firstResponse.Rows.Single().Identity.GameName.Should().Be("First");

        // Sneak a second account into the DB. If the leaderboard wasn't
        // cached, the next request would see Total=2.
        await using (var db = _fixture.CreateDbContext())
        {
            var second = Account("second", "Second", "EUW1");
            db.RiotAccounts.Add(second);
            db.RankSnapshots.Add(Snapshot(second, "MASTER", "I", 200, now));
            db.MainChampionStats.Add(MainStat("second", "EUW1", 86, "TOP", isMain: true));
            await db.SaveChangesAsync();
        }

        // Identical request — must come back from cache, so still Total=1.
        var cachedResponse = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        cachedResponse!.Total.Should().Be(1, "the second request must hit the cache and ignore the freshly inserted account");
        cachedResponse.Rows.Single().Identity.GameName.Should().Be("First");

        // Different filter shape (?region=europe vs default) yields a
        // different cache key. The request bypasses the cache, hits the DB,
        // and now sees both accounts — proving the cache is keyed correctly
        // (not just returning the first response for every request).
        var freshResponse = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?region=europe");
        freshResponse!.Total.Should().Be(2, "a different filter shape must miss the cache");
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
    {
        // Mirror the ingestion writer: a snapshot's rank determines the
        // account's denormalised leaderboard Score, which the query orders on.
        account.Score = RankScore.Compute(tier, division, leaguePoints);
        return new()
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
    }

    private static MainChampionStat MainStat(string puuid, string platformId, int championId, string primaryPosition, bool isMain, int totalMatches = 100)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = totalMatches,
            ChampionMatches = Math.Min(50, totalMatches),
            PlayRate = 0.5d,
            IsMain = isMain,
            IsOtp = false,
            PrimaryPosition = primaryPosition,
            PositionBreakdown = [new PositionStat { Position = primaryPosition, Games = 50, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow,
        };

    private static MainChampionStat MainStatWithBreakdown(
        string puuid,
        string platformId,
        int championId,
        string primaryPosition,
        List<PositionStat> breakdown)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 100,
            ChampionMatches = breakdown.Sum(p => p.Games),
            PlayRate = 0.5d,
            IsMain = true,
            IsOtp = false,
            PrimaryPosition = primaryPosition,
            PositionBreakdown = breakdown,
            CalculatedAtUtc = DateTime.UtcNow,
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

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
                // Most tests seed rank rows without participants, so the
                // MinRankedGames filter is disabled to keep assertions narrow;
                // the dedicated test below uses a non-zero threshold.
                new KeyValuePair<string, string?>("TruemainsLeaderboard:MinRankedGames", "0"),
            ]);

    /// <summary>
    /// Same factory but with a non-zero MinRankedGames threshold so the
    /// dedicated test can verify the filter actually drops low-game rows.
    /// </summary>
    private sealed class ApiWebApplicationFactoryWithMinGames(PostgresFixture fixture, int minRankedGames)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("TruemainsLeaderboard:MinRankedGames", minRankedGames.ToString()),
            ]);
}
