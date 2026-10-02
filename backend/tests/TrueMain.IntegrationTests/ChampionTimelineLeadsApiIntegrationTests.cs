using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionTimelineLeadsApiIntegrationTests
{
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";
    private const string Patch = "16.4";
    private static readonly int[] Intervals = [5, 10, 15, 20, 30];

    // A non-canonical minute the aggregate also carries (every minute 1..30 is
    // stored now); the leads read must pin the canonical marks and drop it.
    private const int NonCanonicalMinute = 7;

    // Each interval's stored totals are the per-game lead times the game count, so
    // the averaged lead is exactly the lead regardless of game count.
    private const int GoldLead = 500;
    private const int CsLead = 5;
    private const int KillsLead = 1;
    private const int LevelLead = 1;
    private const int XpLead = 200;
    private const int DamageLead = 300;

    private static readonly DateTime AggregatedAt = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly PostgresFixture _fixture;

    public ChampionTimelineLeadsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionTimelineLeadsAsync_AveragesLeadVsLaneOpponentPerInterval()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedLeadSampleAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/timeline-leads?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var leads = await response.Content.ReadFromJsonAsync<ChampionTimelineLeadsResponse>();
        leads.Should().NotBeNull();
        leads!.ChampionId.Should().Be(Champion);
        leads.Position.Should().Be(Position);
        leads.Intervals.Select(i => i.IntervalMinute).Should().Equal(Intervals);

        foreach (var interval in leads.Intervals)
        {
            interval.Games.Should().Be(12);
            interval.GoldDiff.Should().BeApproximately(GoldLead, 1e-9);
            interval.CsDiff.Should().BeApproximately(CsLead, 1e-9);
            interval.KillsDiff.Should().BeApproximately(KillsLead, 1e-9);
            interval.LevelDiff.Should().BeApproximately(LevelLead, 1e-9);
            interval.XpDiff.Should().BeApproximately(XpLead, 1e-9);
            interval.DamageDiff.Should().BeApproximately(DamageLead, 1e-9);
        }
    }

    [Fact]
    public async Task GetChampionTimelineLeadsAsync_DropsIntervalsBelowSampleFloor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedLeadSampleAsync(games: 5); // below MinMatchupGames floor of 10

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/timeline-leads?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var leads = await response.Content.ReadFromJsonAsync<ChampionTimelineLeadsResponse>();
        leads!.Intervals.Should().BeEmpty("five games is below the sample floor");
    }

    [Fact]
    public async Task GetChampionTimelineLeadsAsync_FiltersToRequestedPatch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedLeadSampleAsync(games: 12); // all seeded on patch 16.4

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var onPatch = await client.GetAsync($"/champions/{Champion}/timeline-leads?position={Position}&patch=16.4");
        onPatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var matched = await onPatch.Content.ReadFromJsonAsync<ChampionTimelineLeadsResponse>();
        matched!.Patch.Should().Be("16.4");
        matched.Intervals.Should().HaveCount(Intervals.Length, "the seeded rows are on patch 16.4");

        var offPatch = await client.GetAsync($"/champions/{Champion}/timeline-leads?position={Position}&patch=16.5");
        offPatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var missed = await offPatch.Content.ReadFromJsonAsync<ChampionTimelineLeadsResponse>();
        missed!.Intervals.Should().BeEmpty("no rows were seeded on 16.5");
    }

    [Fact]
    public async Task GetChampionTimelineLeadsAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/timeline-leads?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Seeds champion_timeline_lead_stats directly (the read is served from it): the
    // five canonical marks plus one non-canonical minute the read must drop.
    private async Task SeedLeadSampleAsync(int games)
    {
        await using var db = _fixture.CreateDbContext();

        foreach (var minute in Intervals.Append(NonCanonicalMinute))
        {
            db.ChampionTimelineLeadStats.Add(new ChampionTimelineLeadStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                Patch = Patch,
                IntervalMinute = minute,
                Games = games,
                TotalGoldDiff = (long)GoldLead * games,
                TotalCsDiff = (long)CsLead * games,
                TotalKillsDiff = (long)KillsLead * games,
                TotalLevelDiff = (long)LevelLead * games,
                TotalXpDiff = (long)XpLead * games,
                TotalDamageDiff = (long)DamageLead * games,
                AggregatedAtUtc = AggregatedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinMatchupGames", "10"),
                new KeyValuePair<string, string?>("ChampionsList:MinPlayerMatchupGames", "3"),
            ]);
}
