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
