using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionSummariesApiIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ChampionSummariesApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListChampionsAsync_ReturnsAllSummariesForActivePatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedSummariesAcrossManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summaries = await response.Content.ReadFromJsonAsync<IReadOnlyList<ChampionSummaryReadModel>>();
        summaries.Should().NotBeNull();
        summaries!.Should().HaveCount(60, "the seeder writes 60 (champion, position) pairs and the endpoint streams them all in one payload");

        var keys = summaries.Select(item => (item.ChampionId, item.Position)).ToHashSet();
        keys.Should().HaveCount(60, "every (champion, position) pair is unique in the seed");
    }

    [Fact]
    public async Task ListChampionsAsync_ComputesAggregatedFieldsForKnownSlice()
    {
        await _fixture.ResetDatabaseAsync();
        var seededAtUtc = await SeedSummariesAcrossManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summaries = await response.Content.ReadFromJsonAsync<IReadOnlyList<ChampionSummaryReadModel>>();
        summaries.Should().NotBeNull();

        // Verify the computed/aggregated fields for one known seeded slice so a
        // regression in the SQL GROUP BY (SUM/COUNT DISTINCT/MAX) or in the
        // server-side type translation/casting would be caught here, not just
        // by the row-count assertion above.
        //
        // The seeder loop writes, for iteration i (0..59):
        //   ChampionId        = 100 + i
        //   Position          = positions[i % 5]   (TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY)
        //   Games             = 10 + i
        //   Wins              = 5 + (i % 4)
        //   RiotAccountId     = the single seeded account
        //   AggregatedAtUtc   = seededAtUtc.AddMinutes(-i)
        // Each (champion, position) pair is produced by exactly one iteration
        // (ChampionId is unique per i), so each summary row maps to a single
        // seed scope with no fan-in within the slice.
        //
        // Pick i = 0 -> ChampionId 100, Position TOP:
        //   Games = 10, Wins = 5, TrueMainCount = 1 (one distinct account),
        //   AggregatedAtUtc = seededAtUtc (AddMinutes(0)).
        const int championId = 100; // i = 0
        const string position = "TOP";

        var slice = summaries!.SingleOrDefault(item => item.ChampionId == championId && item.Position == position);
        slice.Should().NotBeNull("the seed writes exactly one (champion 100, TOP) slice");

        slice!.Games.Should().Be(10, "SUM(Games) over the single seeded scope for champion 100/TOP");
        slice.Wins.Should().Be(5, "SUM(Wins) over the single seeded scope for champion 100/TOP");
        slice.TrueMainCount.Should().Be(1, "COUNT(DISTINCT RiotAccountId) is the single seeded account");
        slice.WinRate.Should().BeApproximately(0.5, 1e-9, "Wins / Games = 5 / 10");

        // PickRate = this slice's Games / total TOP games on the patch.
        // TOP slices are the iterations where i % 5 == 0 (i = 0,5,...,55),
        // each contributing Games = 10 + i, so the lane total is:
        //   sum over {0,5,...,55} of (10 + i) = 450.
        var topLaneTotal = Enumerable.Range(0, 60)
            .Where(i => i % 5 == 0)
            .Sum(i => 10 + i);
        topLaneTotal.Should().Be(450);
        slice.PickRate.Should().BeApproximately(10.0 / topLaneTotal, 1e-9,
            "PickRate = slice Games / total Games at the same lane");

        // LanePlayRate = this slice's Games / total games for this champion
        // across all lanes. Champion 100 only ever appears at TOP, so the
        // champion total equals this slice's Games and the rate is 1.0.
        slice.LanePlayRate.Should().BeApproximately(1.0, 1e-9,
            "champion 100 only appears at TOP, so its lane play rate is 1.0");

        // LastUpdatedAtUtc = MAX(AggregatedAtUtc). For i = 0 that is
        // seededAtUtc.AddMinutes(0). Round-trips through JSON (UTC), so compare
        // with a small tolerance and normalise the kind.
        slice.LastUpdatedAtUtc.ToUniversalTime().Should().BeCloseTo(
            seededAtUtc, TimeSpan.FromMilliseconds(1),
            "MAX(AggregatedAtUtc) over the single seeded scope for champion 100/TOP");

        slice.PatchVersion.Should().Be("16.5", "the slice belongs to the active patch the seed wrote");

        // Tier is computed server-side and bucketed by patch-wide percentile,
        // so every row of a populated patch carries one of the five letters.
        // Assert the whole payload (not just this slice) so a serialization or
        // wiring regression that drops the field surfaces here.
        var validTiers = new[] { "S", "A", "B", "C", "D" };
        summaries!.Should().OnlyContain(item => validTiers.Contains(item.Tier),
            "ChampionTierCalculator stamps every row with an S/A/B/C/D tier");

        // The seed's Games (and thus winRate / pickRate) climb with i, so the
        // strongest rows must reach the top tier and the weakest the bottom —
        // proof the percentile bucketing actually spread the field.
        summaries.Select(item => item.Tier).Should().Contain("S")
            .And.Contain("D", "a 60-row patch spans the full pyramid");
    }

    [Fact]
    public async Task ListChampionsAsync_DropsLinesBelowTheSampleFloor()
    {
        await _fixture.ResetDatabaseAsync();

        // One well-sampled (champion, lane) line and one below the 20-game floor.
        // Only the well-sampled one should surface: a 3-game line is noise that
        // would otherwise fluke to the top/bottom of the tier percentiles.
        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = accountId,
                PlatformId = "KR",
                Puuid = "summaries-puuid-floor",
                GameName = "summaries-floor",
                SummonerId = "summaries-floor-summoner",
                ProfileIconId = 1,
                SummonerLevel = 100,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-1),
            });
            await db.SaveChangesAsync();

            var seeder = new ChampionAggregateSeeder();
            // Comfortably above the floor.
            seeder.AddPatternWithRune(
                accountId, 200, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 40, wins: 22, aggregatedAtUtc: now);
            // Below the floor.
            seeder.AddPatternWithRune(
                accountId, 201, "16.5", "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 3, wins: 3, aggregatedAtUtc: now);
            await seeder.SaveAsync(db);
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, minSampleGames: 20);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summaries = await response.Content.ReadFromJsonAsync<IReadOnlyList<ChampionSummaryReadModel>>();
        summaries.Should().NotBeNull();

        var championIds = summaries!.Select(item => item.ChampionId).ToHashSet();
        championIds.Should().Contain(200, "40 games clears the 20-game sample floor");
        championIds.Should().NotContain(201,
            "a 3-game line is below the floor, so it is dropped from the list and the ranking");
    }

    // The two aggregation tests above seed a few low-game slices on purpose, so
    // they disable the sample floor (0) to assert over every seeded row. The
    // dedicated floor test drives it at a real threshold instead.
    private ApiWebApplicationFactory CreateFactory() => new(_fixture, minSampleGames: 0);

    private async Task<DateTime> SeedSummariesAcrossManyChampionsAsync()
    {
        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = accountId,
            PlatformId = "KR",
            Puuid = "summaries-puuid-1",
            GameName = "summaries-one",
            SummonerId = "summaries-one-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        // 60 (champion, position) pairs so the response covers a realistic
        // directory size. Patch 16.5 + queue 420 matches the MainAnalysis
        // test override used by ApiWebApplicationFactory below.
        var seeder = new ChampionAggregateSeeder();
        var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        for (var i = 0; i < 60; i++)
        {
            var championId = 100 + i;
            var position = positions[i % positions.Length];
            seeder.AddPatternWithRune(
                accountId, championId, "16.5", "KR", 420, position,
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 10 + i, wins: 5 + (i % 4),
                aggregatedAtUtc: now.AddMinutes(-i));
        }

        await seeder.SaveAsync(db);
        return now;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, int minSampleGames)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinSampleGames", minSampleGames.ToString()),
            ]);
}
