using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TrueMain.IntegrationTests;

public sealed class PipelineHealthApiIntegrationTests : IClassFixture<PostgresFixture>
{
    private const string OpsApiKey = "test-ops-key";
    private readonly PostgresFixture _fixture;

    public PipelineHealthApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetPipelineHealthAsync_ShouldReturnProcessAndFreshnessSignals()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedPipelineHealthAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);

        var response = await client.GetAsync("/ops/pipeline-health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["processes", "rawData", "gaps"]);

        var payload = await response.Content.ReadFromJsonAsync<PipelineHealthResponseTestContract>();
        payload.Should().NotBeNull();
        payload!.Processes.Should().Contain(process => process.ProcessName == "MatchIngestion" && process.Status == "success");
        payload.RawData.QueueId.Should().Be(420);
        payload.RawData.RawMatchCount.Should().Be(3);
        payload.RawData.RawParticipantCount.Should().Be(3);
        payload.RawData.Platforms.Should().HaveCount(2);
        payload.RawData.Platforms.Should().Contain(platform => platform.PlatformId == "EUW1" && platform.LatestPatchVersion == "16.4");
        payload.RawData.Platforms.Should().Contain(platform => platform.PlatformId == "KR" && platform.LatestPatchVersion == "16.4");
        payload.Gaps.MatchIngestionToMainAnalysisMinutes.Should().NotBeNull();
        payload.Gaps.ChampionDataLagMinutes.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPipelineHealthAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/pipeline-health");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SeedPipelineHealthAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.ProcessRuns.AddRange(
            BuildProcessRun("Discovery", ProcessRunStatus.Success, now.AddMinutes(-40), now.AddMinutes(-39)),
            BuildProcessRun("Scoring", ProcessRunStatus.Success, now.AddMinutes(-35), now.AddMinutes(-34)),
            BuildProcessRun("MatchIngestion", ProcessRunStatus.Success, now.AddMinutes(-30), now.AddMinutes(-28)),
            BuildProcessRun("MainAnalysis", ProcessRunStatus.Success, now.AddMinutes(-20), now.AddMinutes(-18)),
            BuildProcessRun("AccountRefresh", ProcessRunStatus.Success, now.AddMinutes(-10), now.AddMinutes(-9)));

        db.MainChampionStats.Add(
            new MainChampionStat
            {
                PlatformId = "KR",
                Puuid = "ops-puuid-1",
                ChampionId = 22,
                TotalMatches = 10,
                ChampionMatches = 7,
                PlayRate = 0.7,
                IsMain = true,
                IsOtp = false,
                PrimaryPosition = "BOTTOM",
                CalculatedAtUtc = now.AddHours(-3)
            });

        db.Matches.AddRange(
            new Match
            {
                Id = "OPS_1",
                PlatformId = "KR",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-2),
                GameDurationSeconds = 1800,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddHours(-2),
                TimelineIngested = true
            },
            new Match
            {
                Id = "OPS_2",
                PlatformId = "KR",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-1),
                GameDurationSeconds = 1820,
                GameVersion = "16.4.2",
                CreatedAtUtc = now.AddHours(-1),
                TimelineIngested = true
            },
            new Match
            {
                Id = "OPS_3",
                PlatformId = "EUW1",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddMinutes(-45),
                GameDurationSeconds = 1750,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddMinutes(-45),
                TimelineIngested = true
            },
            new Match
            {
                Id = "OPS_4",
                PlatformId = "KR",
                QueueId = 450,
                MapId = 12,
                GameMode = "ARAM",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now,
                GameDurationSeconds = 1200,
                GameVersion = "16.5.1",
                CreatedAtUtc = now,
                TimelineIngested = true
            });

        db.MatchParticipants.AddRange(
            new MatchParticipant
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999991"),
                MatchId = "OPS_1",
                ParticipantId = 1,
                Puuid = "ops-puuid-1",
                SummonerName = "ops-puuid-1",
                SummonerLevel = 100,
                ChampionId = 22,
                TeamId = 100,
                TeamPosition = "BOTTOM",
                IndividualPosition = "BOTTOM",
                Lane = "BOTTOM",
                Role = "CARRY",
                Win = true,
                Kills = 1,
                Deaths = 1,
                Assists = 1,
                GoldEarned = 10000,
                TotalMinionsKilled = 100,
                NeutralMinionsKilled = 0,
                ChampLevel = 14,
                Item0 = 6672,
                Item1 = 3006,
                Item2 = 0,
                Item3 = 0,
                Item4 = 0,
                Item5 = 0,
                Item6 = 3363,
                TrinketItemId = 3363,
                PerksDefense = 5002,
                PerksFlex = 5008,
                PerksOffense = 5005,
                PrimaryStyleId = 8000,
                SubStyleId = 8200,
                Summoner1Id = 4,
                Summoner2Id = 7,
                ItemEvents = [],
                SkillEvents = []
            },
            new MatchParticipant
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999992"),
                MatchId = "OPS_2",
                ParticipantId = 1,
                Puuid = "ops-puuid-2",
                SummonerName = "ops-puuid-2",
                SummonerLevel = 100,
                ChampionId = 22,
                TeamId = 100,
                TeamPosition = "BOTTOM",
                IndividualPosition = "BOTTOM",
                Lane = "BOTTOM",
                Role = "CARRY",
                Win = false,
                Kills = 1,
                Deaths = 1,
                Assists = 1,
                GoldEarned = 10000,
                TotalMinionsKilled = 100,
                NeutralMinionsKilled = 0,
                ChampLevel = 14,
                Item0 = 6672,
                Item1 = 3006,
                Item2 = 0,
                Item3 = 0,
                Item4 = 0,
                Item5 = 0,
                Item6 = 3363,
                TrinketItemId = 3363,
                PerksDefense = 5002,
                PerksFlex = 5008,
                PerksOffense = 5005,
                PrimaryStyleId = 8000,
                SubStyleId = 8200,
                Summoner1Id = 4,
                Summoner2Id = 7,
                ItemEvents = [],
                SkillEvents = []
            },
            new MatchParticipant
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999993"),
                MatchId = "OPS_3",
                ParticipantId = 1,
                Puuid = "ops-puuid-3",
                SummonerName = "ops-puuid-3",
                SummonerLevel = 100,
                ChampionId = 22,
                TeamId = 100,
                TeamPosition = "BOTTOM",
                IndividualPosition = "BOTTOM",
                Lane = "BOTTOM",
                Role = "CARRY",
                Win = true,
                Kills = 1,
                Deaths = 1,
                Assists = 1,
                GoldEarned = 10000,
                TotalMinionsKilled = 100,
                NeutralMinionsKilled = 0,
                ChampLevel = 14,
                Item0 = 6672,
                Item1 = 3006,
                Item2 = 0,
                Item3 = 0,
                Item4 = 0,
                Item5 = 0,
                Item6 = 3363,
                TrinketItemId = 3363,
                PerksDefense = 5002,
                PerksFlex = 5008,
                PerksOffense = 5005,
                PrimaryStyleId = 8000,
                SubStyleId = 8200,
                Summoner1Id = 4,
                Summoner2Id = 7,
                ItemEvents = [],
                SkillEvents = []
            },
            new MatchParticipant
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999994"),
                MatchId = "OPS_4",
                ParticipantId = 1,
                Puuid = "ops-puuid-4",
                SummonerName = "ops-puuid-4",
                SummonerLevel = 100,
                ChampionId = 22,
                TeamId = 100,
                TeamPosition = "BOTTOM",
                IndividualPosition = "BOTTOM",
                Lane = "BOTTOM",
                Role = "CARRY",
                Win = true,
                Kills = 1,
                Deaths = 1,
                Assists = 1,
                GoldEarned = 10000,
                TotalMinionsKilled = 100,
                NeutralMinionsKilled = 0,
                ChampLevel = 14,
                Item0 = 6672,
                Item1 = 3006,
                Item2 = 0,
                Item3 = 0,
                Item4 = 0,
                Item5 = 0,
                Item6 = 3363,
                TrinketItemId = 3363,
                PerksDefense = 5002,
                PerksFlex = 5008,
                PerksOffense = 5005,
                PrimaryStyleId = 8000,
                SubStyleId = 8200,
                Summoner1Id = 4,
                Summoner2Id = 7,
                ItemEvents = [],
                SkillEvents = []
            });

        await db.SaveChangesAsync();
    }

    private static ProcessRun BuildProcessRun(string processName, ProcessRunStatus status, DateTime startedAtUtc, DateTime finishedAtUtc)
    {
        return new ProcessRun
        {
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = finishedAtUtc,
            DurationMs = (int)(finishedAtUtc - startedAtUtc).TotalMilliseconds,
            Status = status,
            Host = "test-host"
        };
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
                    new KeyValuePair<string, string?>("Ops:ApiKey", OpsApiKey)
                ]);
            });
        }
    }

    private sealed class PipelineHealthResponseTestContract
    {
        public IReadOnlyList<ProcessHealthTestContract> Processes { get; init; } = [];

        public RawDataFreshnessTestContract RawData { get; init; } = new();

        public PipelineGapTestContract Gaps { get; init; } = new();
    }

    private sealed class ProcessHealthTestContract
    {
        public string ProcessName { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;
    }

    private sealed class RawDataFreshnessTestContract
    {
        public int QueueId { get; init; }

        public int RawMatchCount { get; init; }

        public int RawParticipantCount { get; init; }

        public IReadOnlyList<PlatformRawDataFreshnessTestContract> Platforms { get; init; } = [];
    }

    private sealed class PlatformRawDataFreshnessTestContract
    {
        public string PlatformId { get; init; } = string.Empty;

        public string LatestPatchVersion { get; init; } = string.Empty;
    }

    private sealed class PipelineGapTestContract
    {
        public double? MatchIngestionToMainAnalysisMinutes { get; init; }

        public double? ChampionDataLagMinutes { get; init; }
    }
}
