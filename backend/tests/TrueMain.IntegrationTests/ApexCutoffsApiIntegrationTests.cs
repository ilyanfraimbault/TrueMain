using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Ladder;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for <c>GET /ladder/{platformId}/apex-cutoffs</c> (#2174), the Grandmaster
/// and Challenger lines of the ranked card's LP curve: one entry per measured day, oldest
/// first, scoped to the platform asked for and to the window.
/// </summary>
/// <remarks>
/// As in <see cref="RankHistoryApiIntegrationTests"/>, the window edge is seeded with days of
/// margin: the service builds its lower bound from the wall clock at request time.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class ApexCutoffsApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public ApexCutoffsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Returns400ForAnUnknownPlatform()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ladder/MOON1/apex-cutoffs");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReturnsAnEmptyListWhenNothingWasMeasured()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<ApexCutoffsReadModel>("/ladder/EUW1/apex-cutoffs");

        payload!.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsOneEntryPerDayOldestFirstForThePlatformAndWindow()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = _fixture.CreateDbContext())
        {
            db.ApexCutoffSnapshots.AddRange(
                Cutoff("EUW1", RankTier.Grandmaster, today, 402),
                Cutoff("EUW1", RankTier.Challenger, today, 871),
                Cutoff("EUW1", RankTier.Grandmaster, today.AddDays(-3), 395),
                Cutoff("EUW1", RankTier.Challenger, today.AddDays(-40), 850),
                Cutoff("KR", RankTier.Grandmaster, today, 610));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ladder/EUW1/apex-cutoffs?days=30");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The chart reads the day as a `YYYY-MM-DD` string and the LP under these names.
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var first = document.RootElement.GetProperty("entries")[0];
        first.GetProperty("dateUtc").GetString().Should().Be(today.AddDays(-3).ToString("yyyy-MM-dd"));
        first.GetProperty("grandmasterMinLp").GetInt32().Should().Be(395);
        first.GetProperty("challengerMinLp").ValueKind.Should().Be(JsonValueKind.Null);

        var payload = JsonSerializer.Deserialize<ApexCutoffsReadModel>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        payload.Entries.Select(entry => (entry.DateUtc, entry.GrandmasterMinLp, entry.ChallengerMinLp)).Should().Equal(
            (today.AddDays(-3), 395, null),
            (today, 402, 871));
    }

    private static ApexCutoffSnapshot Cutoff(string platformId, RankTier tier, DateOnly day, int minLeaguePoints) => new()
    {
        PlatformId = platformId,
        Tier = tier,
        CapturedOnUtc = day,
        MinLeaguePoints = minLeaguePoints,
        EntryCount = 300,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);
}
