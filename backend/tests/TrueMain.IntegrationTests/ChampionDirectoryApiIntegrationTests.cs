using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// <c>GET /champions/directory</c> (#1734): the paged, sorted, filtered view over the same
/// cached directory as <c>GET /champions</c>. The order rules themselves are pinned in
/// <c>ChampionDirectoryQueryServiceTests</c>; this covers the wire contract — defaults,
/// paging, filters, the 400s and the lenient sort.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionDirectoryApiIntegrationTests : IAsyncLifetime
{
    private const int SeededLines = 60;

    private readonly PostgresFixture _fixture;

    public ChampionDirectoryApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetDirectoryPageAsync_defaults_to_the_first_fifty_lines_by_pick_rate()
    {
        await SeedLinesAsync();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var page = await GetPageAsync(client, "/champions/directory");

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(50);
        page.Total.Should().Be(SeededLines);
        page.PatchVersion.Should().Be("16.5");
        page.Rows.Should().HaveCount(50);
        page.Rows.Select(row => row.PickRate).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetDirectoryPageAsync_pages_through_every_line_once()
    {
        await SeedLinesAsync();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var first = await GetPageAsync(client, "/champions/directory?pageSize=40");
        var second = await GetPageAsync(client, "/champions/directory?pageSize=40&page=2");
        var past = await GetPageAsync(client, "/champions/directory?pageSize=40&page=3");

        second.Rows.Should().HaveCount(SeededLines - 40);
        first.Rows.Concat(second.Rows)
            .Select(row => (row.ChampionId, row.Position))
            .Should().OnlyHaveUniqueItems().And.HaveCount(SeededLines);
        past.Rows.Should().BeEmpty();
        past.Total.Should().Be(SeededLines, "an empty page past the end still reports the real total");
    }

    [Fact]
    public async Task GetDirectoryPageAsync_orders_by_the_requested_column_and_direction()
    {
        await SeedLinesAsync();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var page = await GetPageAsync(client, "/champions/directory?sort=games&order=asc&pageSize=100");

        page.Rows.Select(row => row.Games).Should().BeInAscendingOrder();
        page.Rows[0].ChampionId.Should().Be(100, "the seed's first line has the fewest games");
    }

    [Fact]
    public async Task GetDirectoryPageAsync_falls_back_to_the_default_order_on_an_unknown_sort()
    {
        await SeedLinesAsync();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var page = await GetPageAsync(client, "/champions/directory?sort=name&order=sideways");

        page.Rows.Select(row => row.PickRate).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetDirectoryPageAsync_narrows_to_a_lane_and_to_a_champion()
    {
        await SeedLinesAsync();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var topLane = await GetPageAsync(client, "/champions/directory?position=TOP");
        topLane.Total.Should().Be(SeededLines / 5);
        topLane.Rows.Should().OnlyContain(row => row.Position == "TOP");

        var oneChampion = await GetPageAsync(client, "/champions/directory?championId=105");
        oneChampion.Total.Should().Be(1);
        oneChampion.Rows.Should().ContainSingle().Which.ChampionId.Should().Be(105);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("championId=0")]
    [InlineData("position=FOO")]
    [InlineData("eloBracket=JUNK")]
    public async Task GetDirectoryPageAsync_rejects_an_invalid_parameter(string query)
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/directory?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<ChampionDirectoryPageReadModel> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ChampionDirectoryPageReadModel>();
        page.Should().NotBeNull();
        return page!;
    }

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    /// <summary>
    /// The directory test seed (<c>ChampionSummariesApiIntegrationTests</c>): one account,
    /// 60 champions on one lane each, games climbing with the index.
    /// </summary>
    private async Task SeedLinesAsync()
    {
        var now = DateTime.UtcNow;
        var accountId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = accountId,
            PlatformId = "KR",
            Puuid = "directory-puuid-1",
            GameName = "directory-one",
            SummonerId = "directory-one-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var seeder = new ChampionAggregateSeeder();
        var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        for (var i = 0; i < SeededLines; i++)
        {
            seeder.AddPatternWithRune(
                accountId, 100 + i, "16.5", "KR", 420, positions[i % positions.Length],
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 10 + i, wins: 5 + (i % 4),
                aggregatedAtUtc: now.AddMinutes(-i));
        }

        await seeder.SaveAsync(db);
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinSampleGames", "0"),
                new KeyValuePair<string, string?>("ChampionsList:MinServablePatchLines", "0"),
            ]);
}
