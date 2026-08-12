using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers <c>GET /champions/overview</c> (#972) — specifically the concern a
/// unit test can't: that <c>gamesAnalyzed</c> is a true SQL-level sum over
/// every <c>champion_aggregate_scopes</c> row on the patch, including rows the
/// ranked directory itself drops (below the sample floor, or with no
/// <c>Position</c>). Ordering/limit/tier-fallback logic is covered by
/// <c>ChampionOverviewQueryServiceTests</c> against a mocked summaries result.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionOverviewApiIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ChampionOverviewApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetOverviewAsync_SumsEveryAggregatedGame_IncludingBelowFloorAndPositionLessRows()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = accountId,
                PlatformId = "KR",
                Puuid = "overview-puuid-1",
                GameName = "overview-one",
                SummonerId = "overview-one-summoner",
                ProfileIconId = 1,
                SummonerLevel = 100,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-1),
            });
            await db.SaveChangesAsync();

            var seeder = new ChampionAggregateSeeder();
            // Clears the (20-game) sample floor — the only row the ranked
            // directory keeps.
            seeder.AddPatternWithRune(
                accountId, 300, "16.5", "KR", 420, "TOP",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 40, wins: 22, aggregatedAtUtc: now);
            // Below the sample floor — dropped from the ranked directory, but
            // its games must still count toward gamesAnalyzed.
            seeder.AddPatternWithRune(
                accountId, 301, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 3, wins: 1, aggregatedAtUtc: now);
            // No position — always excluded from the ranked directory (no lane
            // to score), but its games must still count toward gamesAnalyzed.
            seeder.AddPatternWithRune(
                accountId, 302, "16.5", "KR", 420, string.Empty,
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 15, wins: 9, aggregatedAtUtc: now);
            await seeder.SaveAsync(db);
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, minSampleGames: 20);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var overview = await response.Content.ReadFromJsonAsync<ChampionOverviewReadModel>();
        overview.Should().NotBeNull();

        overview!.GamesAnalyzed.Should().Be(40 + 3 + 15,
            "the total sums every seeded scope's games, not just the one row that clears the sample floor and has a position");
        overview.ChampionsRanked.Should().Be(1, "only champion 300 clears the sample floor with a position");
        overview.TopRows.Should().ContainSingle(row => row.ChampionId == 300);
        overview.TopRows.Should().NotContain(row => row.ChampionId == 301 || row.ChampionId == 302,
            "below-floor and position-less rows never enter the ranked directory, even though they're counted in the total");
    }

    [Fact]
    public async Task GetOverviewAsync_LimitQueryParamTruncatesTopRows()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = accountId,
                PlatformId = "KR",
                Puuid = "overview-puuid-2",
                GameName = "overview-two",
                SummonerId = "overview-two-summoner",
                ProfileIconId = 1,
                SummonerLevel = 100,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-1),
            });
            await db.SaveChangesAsync();

            var seeder = new ChampionAggregateSeeder();
            var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            for (var i = 0; i < 10; i++)
            {
                seeder.AddPatternWithRune(
                    accountId, 400 + i, "16.5", "KR", 420, positions[i % positions.Length],
                    summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                    buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                    primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                    games: 30 + i, wins: 15 + i, aggregatedAtUtc: now.AddMinutes(-i));
            }
            await seeder.SaveAsync(db);
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, minSampleGames: 0);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var defaultResponse = await client.GetAsync("/champions/overview");
        var defaultOverview = await defaultResponse.Content.ReadFromJsonAsync<ChampionOverviewReadModel>();
        defaultOverview!.TopRows.Should().HaveCount(8, "the default limit is 8");

        var limitedResponse = await client.GetAsync("/champions/overview?limit=3");
        var limitedOverview = await limitedResponse.Content.ReadFromJsonAsync<ChampionOverviewReadModel>();
        limitedOverview!.TopRows.Should().HaveCount(3);

        var clampedResponse = await client.GetAsync("/champions/overview?limit=999");
        var clampedOverview = await clampedResponse.Content.ReadFromJsonAsync<ChampionOverviewReadModel>();
        clampedOverview!.TopRows.Should().HaveCount(10, "only 10 rows were seeded, well under the 20-row clamp ceiling");
    }

    [Fact]
    public async Task GetOverviewAsync_SkipsAPatchTooThinToRank_AndSumsTheChipsAcrossTheWindow()
    {
        // The #1109 regression end to end. 16.16 exists and holds aggregate rows, but
        // not one of its lines clears the sample floor: serving it printed an empty
        // directory, an empty tier list and a two-digit "games analyzed" on the
        // homepage while 16.15 sat beside it with a full patch of data.
        await _fixture.ResetDatabaseAsync();
        await SeedThinNewPatchAsync(Guid.Parse("77777777-7777-7777-7777-777777777777"), "overview-puuid-3");

        await using var factory = new ApiWebApplicationFactory(
            _fixture, minSampleGames: 20, minServablePatchLines: 5);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var overview = await response.Content.ReadFromJsonAsync<ChampionOverviewReadModel>();
        overview.Should().NotBeNull();

        overview!.PatchVersion.Should().Be("16.15",
            "16.16 holds rows but none clear the floor, so it cannot fill a directory yet");
        overview.TopRows.Should().NotBeEmpty("the whole point of the fallback is a populated teaser");
        overview.TopRows.Should().OnlyContain(row => row.ChampionId >= 500 && row.ChampionId <= 505,
            "the teaser is ranked on the served patch alone");

        overview.CountedPatches.Should().Equal("16.15", "16.14");
        overview.GamesAnalyzed.Should().Be((6 * 40) + (3 * 50),
            "the chips span the served patch and the one before it — and never reach forward to 16.16, "
            + "whose games every other surface is refusing to show");
        overview.ChampionsRanked.Should().Be(8,
            "six champions on 16.15 plus three on 16.14, of which champion 505 is on both and counts once");
    }

    [Fact]
    public async Task GetOverviewAsync_KeepsTheFullWindowWhenTheWalkStepsBackTwice()
    {
        // The window has to be measured from where the walk landed, not from a fixed
        // depth: with two thin patches stacked up, a window sized against "one thin
        // patch ahead" returns a single patch while quietly claiming to span two.
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = accountId,
                PlatformId = "KR",
                Puuid = "overview-puuid-5",
                GameName = "overview-five",
                SummonerId = "overview-five-summoner",
                ProfileIconId = 1,
                SummonerLevel = 100,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-1),
            });
            await db.SaveChangesAsync();

            var seeder = new ChampionAggregateSeeder();
            // Two thin patches in front, so the walk has to step back twice.
            Add(seeder, accountId, 800, "16.16", games: 1, wins: 0, now);
            Add(seeder, accountId, 801, "16.15", games: 2, wins: 1, now);
            // 16.14 is served; 16.13 is the second half of the homepage window.
            for (var championId = 810; championId <= 815; championId++)
            {
                Add(seeder, accountId, championId, "16.14", games: 40, wins: 22, now);
            }
            for (var championId = 820; championId <= 822; championId++)
            {
                Add(seeder, accountId, championId, "16.13", games: 50, wins: 25, now);
            }
            await seeder.SaveAsync(db);
        }

        await using var factory = new ApiWebApplicationFactory(
            _fixture, minSampleGames: 20, minServablePatchLines: 5);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var overview = await (await client.GetAsync("/champions/overview"))
            .Content.ReadFromJsonAsync<ChampionOverviewReadModel>();

        overview!.PatchVersion.Should().Be("16.14", "neither 16.16 nor 16.15 can fill a directory");
        overview.CountedPatches.Should().Equal("16.14", "16.13",
            "the window still spans two patches after a two-step walk-back");
        overview.GamesAnalyzed.Should().Be((6 * 40) + (3 * 50));
    }

    [Fact]
    public async Task GetOverviewAsync_WithTheBarDisabled_ServesTheNewestPatchAgain()
    {
        // The documented off-switch: MinServablePatchLines = 0 restores the pre-#1109
        // rule, newest patch with any row at all.
        await _fixture.ResetDatabaseAsync();
        await SeedThinNewPatchAsync(Guid.Parse("88888888-8888-8888-8888-888888888888"), "overview-puuid-4");

        await using var factory = new ApiWebApplicationFactory(
            _fixture, minSampleGames: 20, minServablePatchLines: 0);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var overview = await (await client.GetAsync("/champions/overview"))
            .Content.ReadFromJsonAsync<ChampionOverviewReadModel>();

        overview!.PatchVersion.Should().Be("16.16");
        overview.TopRows.Should().BeEmpty("no 16.16 line clears the floor — the state the bar exists to avoid");
    }

    /// <summary>
    /// Three patches: a brand-new one too thin to rank, a full one behind it, and an
    /// older one so the homepage window has a second patch to sum. Champion 505 is on
    /// both settled patches, so a summed champion count would over-report by one.
    /// </summary>
    private async Task SeedThinNewPatchAsync(Guid accountId, string puuid)
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = accountId,
            PlatformId = "KR",
            Puuid = puuid,
            GameName = puuid,
            SummonerId = puuid + "-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var seeder = new ChampionAggregateSeeder();

        // 16.16 — the patch that just shipped: rows exist, nothing is rankable.
        foreach (var championId in new[] { 600, 601 })
        {
            Add(seeder, accountId, championId, "16.16", games: 1, wins: 1, now);
        }

        // 16.15 — the served patch: six lines past the floor, over the bar of five.
        for (var championId = 500; championId <= 505; championId++)
        {
            Add(seeder, accountId, championId, "16.15", games: 40, wins: 22, now);
        }

        // 16.14 — counted by the chips, ranked by nothing.
        foreach (var championId in new[] { 505, 506, 507 })
        {
            Add(seeder, accountId, championId, "16.14", games: 50, wins: 25, now);
        }

        await seeder.SaveAsync(db);
    }

    private static void Add(
        ChampionAggregateSeeder seeder, Guid accountId, int championId, string patch,
        int games, int wins, DateTime aggregatedAtUtc)
        => seeder.AddPatternWithRune(
            accountId, championId, patch, "KR", 420, "TOP",
            summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
            buildItems: [3153, 3006, 3031], bootsItemId: 3006,
            primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
            games: games, wins: wins, aggregatedAtUtc: aggregatedAtUtc);

    private sealed class ApiWebApplicationFactory(
        PostgresFixture fixture, int minSampleGames, int minServablePatchLines = 0)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinSampleGames", minSampleGames.ToString()),
                // Off by default here so the single-patch cases above keep exercising
                // exactly what they were written for.
                new KeyValuePair<string, string?>(
                    "ChampionsList:MinServablePatchLines", minServablePatchLines.ToString()),
            ]);
}
