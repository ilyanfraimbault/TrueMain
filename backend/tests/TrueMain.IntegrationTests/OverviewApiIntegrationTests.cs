using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class OverviewApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public OverviewApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorpusCounters()
    {
        await SeedOverviewAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);

        var response = await client.GetAsync("/ops/stats/overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "trackedAccounts",
                "totalMatches",
                "totalParticipants",
                "candidatesByStatus",
                "totalMains",
                "totalOtps",
                "distinctChampionsWithGames",
                "distinctChampionsWithMains",
                "matchesLast7Days",
                "matchesLast30Days",
                "estimatesUnknownReason",
                "computedAtUtc"
            ]);

        var payload = await response.Content.ReadFromJsonAsync<OverviewTestContract>();
        payload.Should().NotBeNull();
        payload!.TrackedAccounts.Should().Be(2);
        // Planner estimates (#2301): the statistics of a freshly seeded table trail its rows,
        // so only their presence is pinned here — the figure itself is the catalog's.
        payload.TotalMatches.Should().NotBeNull();
        payload.TotalParticipants.Should().NotBeNull();
        payload.EstimatesUnknownReason.Should().BeNull();
        payload.TotalMains.Should().Be(2);
        payload.TotalOtps.Should().Be(1);
        // Read from champion_game_counts (#2301), never from match_participants: three
        // champions have a slice there (one of them twice), the zero-game slice does not count.
        payload.DistinctChampionsWithGames.Should().Be(3);
        // Only IsMain rows count, on champions 22 and 64.
        payload.DistinctChampionsWithMains.Should().Be(2);

        // Every defined status is present (zero-filled) and the seeded ones match.
        payload.CandidatesByStatus.Keys.Should().BeEquivalentTo(
            "New", "Scored", "Queued", "Processing", "Validated");
        payload.CandidatesByStatus["New"].Should().Be(2);
        payload.CandidatesByStatus["Validated"].Should().Be(1);

        // Planner estimates of the windows, never above the table's own estimate.
        payload.MatchesLast7Days.Should().NotBeNull();
        payload.MatchesLast30Days.Should().NotBeNull();
        payload.MatchesLast7Days.Should().BeLessThanOrEqualTo(payload.TotalMatches!.Value);
        payload.MatchesLast30Days.Should().BeLessThanOrEqualTo(payload.TotalMatches!.Value);
        payload.ComputedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldRequireOpsApiKey()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/stats/overview");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SeedOverviewAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount("overview-puuid-1", "OverviewOne", "EUW1"),
            BuildAccount("overview-puuid-2", "OverviewTwo", "KR"));

        db.MainCandidates.AddRange(
            BuildCandidate("overview-puuid-1", "EUW1", 22, MainCandidateStatus.New),
            BuildCandidate("overview-puuid-2", "KR", 64, MainCandidateStatus.New),
            BuildCandidate("overview-puuid-1", "EUW1", 64, MainCandidateStatus.Validated));

        db.MainChampionStats.AddRange(
            BuildMainStat("overview-puuid-1", "EUW1", 22, isMain: true, isOtp: true),
            BuildMainStat("overview-puuid-2", "KR", 64, isMain: true, isOtp: false));

        db.Matches.AddRange(
            BuildMatch("OVW_1", "EUW1", now.AddDays(-1)),
            BuildMatch("OVW_2", "KR", now.AddDays(-3)),
            BuildMatch("OVW_3", "EUW1", now.AddDays(-20)));

        db.MatchParticipants.AddRange(
            BuildParticipant("OVW_1", "overview-puuid-1", 22, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")),
            BuildParticipant("OVW_2", "overview-puuid-2", 22, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")),
            BuildParticipant("OVW_3", "overview-puuid-1", 64, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003")));

        db.ChampionGameCounts.AddRange(
            BuildGameCount("EUW1", "MIDDLE", 22, 10),
            BuildGameCount("KR", "MIDDLE", 22, 4),
            BuildGameCount("EUW1", "TOP", 64, 3),
            BuildGameCount("EUW1", "JUNGLE", 11, 1),
            BuildGameCount("KR", "UTILITY", 99, 0));

        await db.SaveChangesAsync();
    }

    private static ChampionGameCount BuildGameCount(string platformId, string position, int championId, long games)
        => new()
        {
            PlatformId = platformId,
            Patch = "16.4",
            QueueId = 420,
            TeamPosition = position,
            ChampionId = championId,
            Games = games,
            RefreshedAtUtc = DateTime.UtcNow
        };

    private static RiotAccount BuildAccount(string puuid, string gameName, string platformId)
        => new()
        {
            Puuid = puuid,
            GameName = gameName,
            TagLine = platformId,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100
        };

    private static MainCandidate BuildCandidate(string puuid, string platformId, int championId, MainCandidateStatus status)
        => new()
        {
            Puuid = puuid,
            PlatformId = platformId,
            ChampionId = championId,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 100000,
            LastPlayTimeUtc = DateTime.UtcNow.AddDays(-1),
            Score = 1.0,
            Status = status
        };

    private static MainChampionStat BuildMainStat(string puuid, string platformId, int championId, bool isMain, bool isOtp)
        => new()
        {
            Puuid = puuid,
            PlatformId = platformId,
            ChampionId = championId,
            TotalMatches = 20,
            ChampionMatches = 15,
            PlayRate = 0.75,
            IsMain = isMain,
            IsOtp = isOtp,
            PrimaryPosition = "MIDDLE",
            CalculatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

    private static Match BuildMatch(string id, string platformId, DateTime startUtc)
        => new MatchBuilder()
            .WithId(id)
            .WithPlatformId(platformId)
            .WithGameVersion("16.4.1")
            .WithGameStartTimeUtc(startUtc)
            .WithCreatedAtUtc(startUtc)
            .WithTimelineIngested()
            .Build();

    private static MatchParticipant BuildParticipant(string matchId, string puuid, int championId, Guid id)
        => new MatchParticipantBuilder()
            .WithId(id)
            .WithMatchId(matchId)
            .WithPuuid(puuid)
            .WithChampionId(championId)
            .Build();

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class OverviewTestContract
    {
        public int TrackedAccounts { get; init; }

        public long? TotalMatches { get; init; }

        public long? TotalParticipants { get; init; }

        public IReadOnlyDictionary<string, int> CandidatesByStatus { get; init; } = new Dictionary<string, int>();

        public int TotalMains { get; init; }

        public int TotalOtps { get; init; }

        public int DistinctChampionsWithGames { get; init; }

        public int DistinctChampionsWithMains { get; init; }

        public long? MatchesLast7Days { get; init; }

        public long? MatchesLast30Days { get; init; }

        public string? EstimatesUnknownReason { get; init; }

        public DateTime ComputedAtUtc { get; init; }
    }
}
