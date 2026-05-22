using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

public sealed class ChampionSummariesApiIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public ChampionSummariesApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListChampionsAsync_ReturnsRequestedPageSlice()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedSummariesAcrossManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // Page 1 with the default page size of 50 should fill exactly 50
        // rows out of the 60 seeded across 60 distinct (champion, position)
        // pairs — the cache holds the full sorted list, the controller
        // slices on the way out.
        var firstPageResponse = await client.GetAsync("/champions?page=1");
        firstPageResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<ChampionSummariesPagedResponse>();
        firstPage.Should().NotBeNull();
        firstPage!.Page.Should().Be(1);
        firstPage.PageSize.Should().Be(50);
        firstPage.TotalCount.Should().Be(60);
        firstPage.Items.Should().HaveCount(50);

        // Page 2 should hold the tail (60 - 50 = 10 rows) without overlapping
        // with page 1. Verify against the (championId, position) keys
        // because the same champion can appear once per position.
        var secondPageResponse = await client.GetAsync("/champions?page=2");
        secondPageResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<ChampionSummariesPagedResponse>();
        secondPage.Should().NotBeNull();
        secondPage!.Page.Should().Be(2);
        secondPage.Items.Should().HaveCount(10);
        secondPage.TotalCount.Should().Be(60);

        var firstKeys = firstPage.Items.Select(item => (item.ChampionId, item.Position)).ToHashSet();
        var secondKeys = secondPage.Items.Select(item => (item.ChampionId, item.Position)).ToHashSet();
        firstKeys.Intersect(secondKeys).Should().BeEmpty("paginated slices must not overlap");
    }

    [Fact]
    public async Task ListChampionsAsync_OutOfRangePageReturnsEmptyItemsWithTotalCount()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedSummariesAcrossManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // Page well past the end (page=99 × default size 50 = skip 4900)
        // returns an empty slice but preserves TotalCount so the UI can
        // re-anchor on the last valid page.
        var response = await client.GetAsync("/champions?page=99");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var paged = await response.Content.ReadFromJsonAsync<ChampionSummariesPagedResponse>();
        paged.Should().NotBeNull();
        paged!.Items.Should().BeEmpty();
        paged.TotalCount.Should().Be(60);
        paged.Page.Should().Be(99);
        paged.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task ListChampionsAsync_ClampsPageSizeWithinBounds()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedSummariesAcrossManyChampionsAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // pageSize=0 (or negative) should fall back to the default of 50.
        var tooSmall = await client.GetFromJsonAsync<ChampionSummariesPagedResponse>("/champions?page=1&pageSize=0");
        tooSmall.Should().NotBeNull();
        tooSmall!.PageSize.Should().Be(50);
        tooSmall.Items.Should().HaveCount(50);

        // pageSize=500 should be clamped down to the 200 maximum.
        var tooLarge = await client.GetFromJsonAsync<ChampionSummariesPagedResponse>("/champions?page=1&pageSize=500");
        tooLarge.Should().NotBeNull();
        tooLarge!.PageSize.Should().Be(200);
        // Only 60 rows seeded, so the whole list fits on one big page.
        tooLarge.Items.Should().HaveCount(60);
    }

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private async Task SeedSummariesAcrossManyChampionsAsync()
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

        // 60 (champion, position) pairs so the default page size of 50
        // produces a non-empty tail page (10 rows) and a non-trivial first
        // page. Patch 16.5 + queue 420 matches the MainAnalysis test
        // override used by ApiWebApplicationFactory below.
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
                // Vary games so the PickRate-desc ordering is deterministic
                // (no ties on the primary sort key).
                games: 10 + i, wins: 5 + (i % 4),
                aggregatedAtUtc: now.AddMinutes(-i));
        }

        await seeder.SaveAsync(db);
    }

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
                    new KeyValuePair<string, string?>("Ops:ApiKey", "integration-tests-ops-key-0123456789-padding")
                ]);
            });
        }
    }
}
