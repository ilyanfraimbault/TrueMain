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

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, int minSampleGames)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinSampleGames", minSampleGames.ToString()),
            ]);
}
