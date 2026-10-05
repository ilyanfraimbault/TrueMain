using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Core.Lol.Map;
using Core.Lol.Pace;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.ReadModels.Benchmarks;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The pace benchmark (#1912) end to end: folded from a timeline while it is ingested,
/// counted once per match, and read back as quartiles per tier and minute.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PaceBenchmarkIntegrationTests : IAsyncLifetime
{
    private const int GameMinutes = 25;

    private readonly PostgresFixture _fixture;

    public PaceBenchmarkIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TimelineIngestion_FoldsTheLobbyAtTheTrackedAccountsTier_Once()
    {
        const string matchId = "EUW1_9001";
        await SeedMatchAsync(matchId, trackedTier: "DIAMOND");

        await IngestTimelineAsync(matchId);

        await using (var db = _fixture.CreateDbContext())
        {
            var stats = await db.PaceBenchmarkStats.AsNoTracking().ToListAsync();
            // Two laners (one tracked, one not), minutes 1..25, two metrics, one sample per bin.
            stats.Sum(stat => stat.Count).Should().Be(2 * GameMinutes * 2);
            stats.Should().OnlyContain(stat => stat.Patch == "16.19" && stat.Tier == "DIAMOND");
            stats.Select(stat => stat.Position).Distinct().Should().BeEquivalentTo(["TOP", "MIDDLE"]);
            stats.Max(stat => stat.Minute).Should().Be(GameMinutes);
            stats.Should().ContainSingle(stat => stat.Position == "MIDDLE" && stat.Minute == 10 && stat.Metric == PaceMetric.Cs)
                .Which.Bucket.Should().Be(PaceHistogram.ToBucket(PaceMetric.Cs, 10 * 9));

            (await db.Matches.SingleAsync(match => match.Id == matchId)).PaceBenchmarkAggregated.Should().BeTrue();

            // A timeline fetched again (it is re-armed here by hand) must not count twice.
            await db.Matches.Where(match => match.Id == matchId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(match => match.TimelineIngested, false));
        }

        await IngestTimelineAsync(matchId);

        await using var verify = _fixture.CreateDbContext();
        (await verify.PaceBenchmarkStats.SumAsync(stat => stat.Count)).Should().Be(2 * GameMinutes * 2);
    }

    [Fact]
    public async Task TimelineIngestion_CountsNothingForALobbyWithoutARankedTrackedAccount()
    {
        const string matchId = "EUW1_9002";
        await SeedMatchAsync(matchId, trackedTier: null);

        await IngestTimelineAsync(matchId);

        await using var db = _fixture.CreateDbContext();
        (await db.PaceBenchmarkStats.AnyAsync()).Should().BeFalse();
        (await db.Matches.SingleAsync(match => match.Id == matchId)).PaceBenchmarkAggregated.Should().BeTrue();
    }

    [Fact]
    public async Task PaceBenchmark_ServesQuartilesAboveTheFloor_OverTheNewestPatches()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            // Minute 10: 30 laners in [60, 65) CS and 30 in [70, 75), all at [4000, 4200) gold.
            db.PaceBenchmarkStats.AddRange(
                Stat("16.19", "DIAMOND", 10, PaceMetric.Cs, 12, 20),
                Stat("16.18", "DIAMOND", 10, PaceMetric.Cs, 12, 10),
                Stat("16.17", "DIAMOND", 10, PaceMetric.Cs, 14, 30),
                Stat("16.19", "DIAMOND", 10, PaceMetric.GoldEarned, 20, 60),
                // Outside the three-patch window: 16.9 is older than 16.17 though it sorts after it.
                Stat("16.9", "DIAMOND", 10, PaceMetric.Cs, 40, 1000),
                Stat("16.9", "DIAMOND", 10, PaceMetric.GoldEarned, 40, 1000),
                // Under the floor.
                Stat("16.19", "DIAMOND", 11, PaceMetric.Cs, 13, 10),
                Stat("16.19", "DIAMOND", 11, PaceMetric.GoldEarned, 21, 10),
                Stat("16.19", "GOLD", 10, PaceMetric.Cs, 10, 60),
                Stat("16.19", "GOLD", 10, PaceMetric.GoldEarned, 18, 60),
                // Another position.
                Stat("16.19", "DIAMOND", 10, PaceMetric.Cs, 2, 500, position: "UTILITY"));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);
        var response = await client.GetFromJsonAsync<PaceBenchmarkResponse>("/benchmarks/pace?position=mid");

        response!.Position.Should().Be("MIDDLE");
        response.Patches.Should().Equal("16.19", "16.18", "16.17");
        response.Tiers.Select(tier => tier.Tier).Should().Equal("GOLD", "DIAMOND");

        var diamond = response.Tiers.Single(tier => tier.Tier == "DIAMOND");
        var minute10 = diamond.Minutes.Single(minute => minute.Minute == 10);
        minute10.Samples.Should().Be(60);
        minute10.Cs.Should().BeEquivalentTo(new PaceQuartilesReadModel { P25 = 62.5, Median = 65, P75 = 72.5 });
        minute10.GoldEarned!.Median.Should().Be(4100);

        var minute11 = diamond.Minutes.Single(minute => minute.Minute == 11);
        minute11.Samples.Should().Be(10);
        minute11.Cs.Should().BeNull();
        minute11.GoldEarned.Should().BeNull();
    }

    [Fact]
    public async Task PaceBenchmark_RejectsAnUnknownPosition()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/benchmarks/pace?position=MID%20LANE");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task IngestTimelineAsync(string matchId)
    {
        await using var db = _fixture.CreateDbContext();
        await using var session = new DataSession(db);
        var service = new TimelineIngestionService(new TimelineClient(), NullLogger<TimelineIngestionService>.Instance);

        var plan = await service.PrepareAsync(session, RegionalRoute.Europe, [matchId], [], CancellationToken.None);
        await using var transaction = await session.BeginTransactionAsync(CancellationToken.None);
        await service.WriteAsync(session, plan, saveBatchSize: 10, CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private async Task SeedMatchAsync(string matchId, string? trackedTier)
    {
        await using var db = _fixture.CreateDbContext();
        var gameStart = new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);

        var account = new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = $"puuid-{matchId}",
            PlatformId = "EUW1",
            CreatedAtUtc = gameStart,
            UpdatedAtUtc = gameStart,
        };
        db.RiotAccounts.Add(account);
        if (trackedTier is not null)
        {
            db.RankSnapshots.Add(new RankSnapshot
            {
                Id = Guid.NewGuid(),
                RiotAccountId = account.Id,
                CapturedAtUtc = gameStart.AddHours(-3),
                Tier = trackedTier,
                Division = "II",
            });
        }

        db.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = "EUW1",
            QueueId = (int)LolQueueId.RankedSoloDuo,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStart,
            GameDurationSeconds = GameMinutes * 60 + 20,
            GameVersion = "16.19.700.1234",
            CreatedAtUtc = gameStart,
        });

        db.MatchParticipants.AddRange(
            Participant(matchId, 1, "TOP", account.Id),
            Participant(matchId, 2, "MIDDLE", riotAccountId: null));

        await db.SaveChangesAsync();
    }

    private static MatchParticipant Participant(string matchId, int participantId, string position, Guid? riotAccountId) => new()
    {
        MatchId = matchId,
        ParticipantId = participantId,
        RiotAccountId = riotAccountId,
        Puuid = $"puuid-{matchId}-{participantId}",
        ChampionId = 100 + participantId,
        TeamId = 100,
        TeamPosition = position,
        IndividualPosition = position,
        ItemEvents = [],
        SkillEvents = [],
    };

    private static PaceBenchmarkStat Stat(
        string patch,
        string tier,
        int minute,
        PaceMetric metric,
        int bucket,
        long count,
        string position = "MIDDLE") => new()
    {
        Patch = patch,
        Tier = tier,
        Position = position,
        Minute = minute,
        Metric = metric,
        Bucket = bucket,
        Count = count,
        AggregatedAtUtc = DateTime.UtcNow,
    };

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);

    /// <summary>
    /// A frame every minute up to the game's end, each participant at 9 CS and 400 gold a
    /// minute — so a value tells which minute it was read at.
    /// </summary>
    private sealed class TimelineClient : IRiotMatchClient
    {
        public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<string>> GetMatchIdsAsync(MatchIdQuery query, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => Task.FromResult(new MatchTimelineDto
            {
                Frames = Enumerable.Range(0, GameMinutes + 1)
                    .Select(minute => minute * 60_000 + 25)
                    .Append(GameMinutes * 60_000 + 20_000)
                    .Select(timestamp => new MatchTimelineFrameDto
                    {
                        TimestampMs = timestamp,
                        ParticipantFrames = Enumerable.Range(1, 2).Select(id => new MatchParticipantFrameDto
                        {
                            ParticipantId = id,
                            MinionsKilled = timestamp / 60_000 * 8,
                            JungleMinionsKilled = timestamp / 60_000,
                            TotalGold = 500 + timestamp / 60_000 * 400,
                        }).ToList(),
                    })
                    .ToList(),
            });
    }
}
