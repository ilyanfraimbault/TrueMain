using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionPowerspikesApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";
    private const string GameVersion = "16.4.521.123";
    private const string Patch = "16.4";
    private const int Games = 12;

    private const int CoreItem = 3153;   // completed item in the dominant build
    private const int NoiseItem = 1001;   // a non-build purchase that must be ignored

    // The gold/damage lead is flat up to this minute, then rises — a deliberate
    // upward kink. Level 6 and the core item completion both sit here, so both
    // events must show a positive spike (the aggregate power curve accelerates
    // right after them).
    private const int KinkMinute = 12;
    private const int MaxMinute = 30;

    private static readonly DateTime AggregatedAt = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly PostgresFixture _fixture;

    public ChampionPowerspikesApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_ReturnsCurveAndPositiveSpikesAtKink()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/powerspikes?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var spikes = await response.Content.ReadFromJsonAsync<ChampionPowerspikesResponse>();
        spikes.Should().NotBeNull();
        spikes!.ChampionId.Should().Be(Champion);
        spikes.Position.Should().Be(Position);

        // The curve is populated (power is computable: the folded sigma moments give
        // a non-zero global spread, so normalization does not divide by zero).
        spikes.Curve.Should().NotBeEmpty();
        spikes.Curve.Should().OnlyContain(point => point.Games == Games);

        // The core build item is detected and shows a positive spike at the kink.
        var itemSpike = spikes.Events.SingleOrDefault(e => e.Type == "item" && e.RefId == CoreItem);
        itemSpike.Should().NotBeNull("the dominant build's completed item is the item event");
        itemSpike!.SpikeMagnitude.Should().BePositive("the power curve accelerates right after the item");
        itemSpike.AvgMinute.Should().BeApproximately(KinkMinute, 0.5);
        itemSpike.Games.Should().Be(Games);

        // The noise purchase (not in the build) must not appear.
        spikes.Events.Should().NotContain(e => e.Type == "item" && e.RefId == NoiseItem);

        // Level 6 is reached at the kink and also spikes positively.
        var level6 = spikes.Events.SingleOrDefault(e => e.Type == "level" && e.RefId == 6);
        level6.Should().NotBeNull();
        level6!.SpikeMagnitude.Should().BePositive();
        level6.AvgMinute.Should().BeApproximately(KinkMinute, 0.5);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/powerspikes?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("SpikeMain")
            .WithTagLine("KR1")
            .WithPuuid("spike-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var minute = 1; minute <= MaxMinute; minute++)
        {
            // Mean lead per minute (flat, then rising) with the sample count baked in.
            db.ChampionTimelineLeadStats.Add(new ChampionTimelineLeadStat
            {
                ChampionId = Champion,
                TeamPosition = Position,
                Patch = Patch,
                IntervalMinute = minute,
                Games = Games,
                TotalGoldDiff = (long)GoldDiffBase(minute) * Games,
                TotalCsDiff = 0,
                TotalKillsDiff = 0,
                TotalLevelDiff = 0,
                TotalXpDiff = 0,
                TotalDamageDiff = (long)DamageDiffBase(minute) * Games,
                AggregatedAtUtc = AggregatedAt,
            });

            // Constant spread across minutes: sigma_gold = sqrt(230000/23) = 100,
            // sigma_dmg = sqrt(57500/23) = 50. Sum = 0 (symmetric population).
            db.TimelineLeadSigmaMoments.Add(new TimelineLeadSigmaMoment
            {
                QueueId = QueueId,
                Patch = Patch,
                IntervalMinute = minute,
                N = 24,
                SumGold = 0,
                SumSqGold = 230_000,
                SumDmg = 0,
                SumSqDmg = 57_500,
                AggregatedAtUtc = AggregatedAt,
            });
        }

        AddEvent(db, "level", 6);
        AddEvent(db, "item", CoreItem);
        AddEvent(db, "item", NoiseItem);

        await db.SaveChangesAsync();

        await new ChampionAggregateSeeder()
            .AddPatternDefaults(
                account.Id, Champion, GameVersion, platformId: "EUW1", QueueId, Position,
                summoner1Id: 4, summoner2Id: 14, skillOrderKey: "Q",
                buildItems: [CoreItem], bootsItemId: 0, games: Games, wins: Games / 2, AggregatedAt)
            .SaveAsync(db);
    }

    private static void AddEvent(TrueMainDbContext db, string type, int refId)
        => db.ChampionPowerspikeEventStats.Add(new ChampionPowerspikeEventStat
        {
            ChampionId = Champion,
            TeamPosition = Position,
            Patch = Patch,
            EventType = type,
            RefId = refId,
            Games = Games,
            SumEventMinute = (long)KinkMinute * Games,
            AggregatedAtUtc = AggregatedAt,
        });

    // Flat lead up to the kink minute, then a linear rise — an upward slope kink.
    private static int GoldDiffBase(int minute)
        => minute <= KinkMinute ? 100 : 100 + (minute - KinkMinute) * 80;

    private static int DamageDiffBase(int minute)
        => minute <= KinkMinute ? 50 : 50 + (minute - KinkMinute) * 40;

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
