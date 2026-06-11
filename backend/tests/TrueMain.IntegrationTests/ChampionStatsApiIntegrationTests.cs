using Core.Lol.Map;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionStatsApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public ChampionStatsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionStatsAsync_ShouldAggregateGamesAndMainCountsUnfiltered()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionStatsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/stats/champions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["championId", "games", "mains", "otps", "extendedSamples"]);

        var rows = await response.Content.ReadFromJsonAsync<IReadOnlyList<ChampionStatRowTestContract>>();
        rows.Should().NotBeNull();

        // Champion 22: 3 participants total (EUW1 16.4 MIDDLE soloq, KR 16.4
        // MIDDLE soloq, EUW1 16.5 BOTTOM flex). One main row (EUW1) + one otp.
        var champion22 = rows!.Single(row => row.ChampionId == 22);
        champion22.Games.Should().Be(3);
        champion22.Mains.Should().Be(1);
        champion22.Otps.Should().Be(1);
        champion22.ExtendedSamples.Should().Be(1);

        // Champion 64: 1 participant, no main_champion_stats row at all.
        var champion64 = rows.Single(row => row.ChampionId == 64);
        champion64.Games.Should().Be(1);
        champion64.Mains.Should().Be(0);
        champion64.Otps.Should().Be(0);

        // Champion 99: a main_champion_stats row with zero games (FULL OUTER JOIN
        // surfaces the mains-only side).
        var champion99 = rows.Single(row => row.ChampionId == 99);
        champion99.Games.Should().Be(0);
        champion99.Mains.Should().Be(1);
    }

    [Fact]
    public async Task GetChampionStatsAsync_ShouldApplyMatchScopedFiltersToGamesButRegionOnlyToMains()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionStatsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // region=EUW1 + patch=16.4 + position=MIDDLE + queue=420 isolates exactly
        // one champion-22 participant (the EUW1 16.4 MIDDLE soloq game). The mains
        // side is region-scoped only, so EUW1's single champion-22 main still shows.
        var response = await client.GetAsync(
            "/ops/stats/champions?region=EUW1&patch=16.4&position=MIDDLE&queue=420");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await response.Content.ReadFromJsonAsync<IReadOnlyList<ChampionStatRowTestContract>>();
        rows.Should().NotBeNull();

        var champion22 = rows!.Single(row => row.ChampionId == 22);
        champion22.Games.Should().Be(1);
        // Region-only scoping: the EUW1 main/otp/extended row is unaffected by the
        // patch/position/queue filters.
        champion22.Mains.Should().Be(1);
        champion22.Otps.Should().Be(1);
        champion22.ExtendedSamples.Should().Be(1);

        // KR-only champion 7 must not appear under region=EUW1.
        rows.Should().NotContain(row => row.ChampionId == 7);
    }

    [Fact]
    public async Task GetChampionStatsAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/stats/champions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private async Task SeedChampionStatsAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.Matches.AddRange(
            BuildMatch("CHS_1", "EUW1", "16.4.1", (int)LolQueueId.RankedSoloDuo, now.AddDays(-1)),
            BuildMatch("CHS_2", "KR", "16.4.1", (int)LolQueueId.RankedSoloDuo, now.AddDays(-1)),
            BuildMatch("CHS_3", "EUW1", "16.5.1", 440, now.AddDays(-1)),
            BuildMatch("CHS_4", "KR", "16.4.1", (int)LolQueueId.RankedSoloDuo, now.AddDays(-1)));

        db.MatchParticipants.AddRange(
            BuildParticipant("CHS_1", participantId: 1, 22, "MIDDLE", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001")),
            BuildParticipant("CHS_2", participantId: 1, 22, "MIDDLE", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")),
            BuildParticipant("CHS_3", participantId: 1, 22, "BOTTOM", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003")),
            // Same match as the first row, so it needs a distinct ParticipantId.
            BuildParticipant("CHS_1", participantId: 2, 64, "JUNGLE", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004")),
            BuildParticipant("CHS_4", participantId: 1, 7, "TOP", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005")));

        db.MainChampionStats.AddRange(
            // EUW1 champion-22 main that is also an OTP and an extended sample.
            BuildMainStat("chs-puuid-1", "EUW1", 22, isMain: true, isOtp: true, isExtendedSample: true),
            // KR champion-7 main (must be region-filtered out for region=EUW1).
            BuildMainStat("chs-puuid-2", "KR", 7, isMain: true, isOtp: false, isExtendedSample: false),
            // EUW1 champion-99 main with no participation rows at all.
            BuildMainStat("chs-puuid-3", "EUW1", 99, isMain: true, isOtp: false, isExtendedSample: false));

        await db.SaveChangesAsync();
    }

    private static Match BuildMatch(string id, string platformId, string gameVersion, int queueId, DateTime startUtc)
        => new()
        {
            Id = id,
            PlatformId = platformId,
            QueueId = queueId,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = startUtc,
            GameDurationSeconds = 1800,
            GameVersion = gameVersion,
            CreatedAtUtc = startUtc,
            TimelineIngested = true
        };

    private static MatchParticipant BuildParticipant(string matchId, int participantId, int championId, string teamPosition, Guid id)
        => new()
        {
            Id = id,
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"chs-{matchId}-{championId}",
            SummonerName = "chs",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = 100,
            TeamPosition = teamPosition,
            IndividualPosition = teamPosition,
            Lane = teamPosition,
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

    private static MainChampionStat BuildMainStat(
        string puuid,
        string platformId,
        int championId,
        bool isMain,
        bool isOtp,
        bool isExtendedSample)
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
            IsExtendedSample = isExtendedSample,
            PrimaryPosition = "MIDDLE",
            CalculatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class ChampionStatRowTestContract
    {
        public int ChampionId { get; init; }

        public long Games { get; init; }

        public int Mains { get; init; }

        public int Otps { get; init; }

        public int ExtendedSamples { get; init; }
    }
}
