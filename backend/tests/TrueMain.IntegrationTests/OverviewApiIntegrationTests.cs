using Core.Lol.Map;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class OverviewApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public OverviewApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorpusCounters()
    {
        await _fixture.ResetDatabaseAsync();
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
                "matchesLast30Days"
            ]);

        var payload = await response.Content.ReadFromJsonAsync<OverviewTestContract>();
        payload.Should().NotBeNull();
        payload!.TrackedAccounts.Should().Be(2);
        payload.TotalMatches.Should().Be(3);
        payload.TotalParticipants.Should().Be(3);
        payload.TotalMains.Should().Be(2);
        payload.TotalOtps.Should().Be(1);
        // Two distinct champions across the three participants (22, 22, 64).
        payload.DistinctChampionsWithGames.Should().Be(2);
        // Only IsMain rows count, on champions 22 and 64.
        payload.DistinctChampionsWithMains.Should().Be(2);

        // Every defined status is present (zero-filled) and the seeded ones match.
        payload.CandidatesByStatus.Should().ContainKeys(
            "New", "Scored", "Queued", "Processing", "Validated", "Rejected");
        payload.CandidatesByStatus["New"].Should().Be(2);
        payload.CandidatesByStatus["Validated"].Should().Be(1);
        payload.CandidatesByStatus["Rejected"].Should().Be(0);

        // Two of the three matches start inside the last 7 days; all three inside 30.
        payload.MatchesLast7Days.Should().Be(2);
        payload.MatchesLast30Days.Should().Be(3);
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

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

        await db.SaveChangesAsync();
    }

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
        => new()
        {
            Id = id,
            PlatformId = platformId,
            QueueId = (int)LolQueueId.RankedSoloDuo,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = startUtc,
            GameDurationSeconds = 1800,
            GameVersion = "16.4.1",
            CreatedAtUtc = startUtc,
            TimelineIngested = true
        };

    private static MatchParticipant BuildParticipant(string matchId, string puuid, int championId, Guid id)
        => new()
        {
            Id = id,
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = puuid,
            SummonerName = puuid,
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = 100,
            TeamPosition = "MIDDLE",
            IndividualPosition = "MIDDLE",
            Lane = "MIDDLE",
            Role = "SOLO",
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
        };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class OverviewTestContract
    {
        public int TrackedAccounts { get; init; }

        public long TotalMatches { get; init; }

        public long TotalParticipants { get; init; }

        public IReadOnlyDictionary<string, int> CandidatesByStatus { get; init; } = new Dictionary<string, int>();

        public int TotalMains { get; init; }

        public int TotalOtps { get; init; }

        public int DistinctChampionsWithGames { get; init; }

        public int DistinctChampionsWithMains { get; init; }

        public long MatchesLast7Days { get; init; }

        public long MatchesLast30Days { get; init; }
    }
}
