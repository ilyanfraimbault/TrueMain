using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the admin Candidates endpoints (<c>GET /ops/candidates</c> +
/// <c>GET /ops/candidates/{id}</c>). The shared seed builds three candidates:
/// one Validated EUW1 candidate whose RiotAccount exists and was brought in by a
/// manual seed request (so detail surfaces the Riot ID, ingested-match count, and
/// the linked request), one New KR candidate with an account but no seed request,
/// and one Scored EUW1 candidate with no account yet (Riot ID null). The list is
/// ordered by score desc; the search/filter facts exercise status, region, Riot
/// ID, PUUID and champion-id matching.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CandidatesApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    // Stable ids so facts can assert on specific rows.
    private static readonly Guid ValidatedCandidateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NewKrCandidateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrphanCandidateId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string ValidatedPuuid = "puuid-validated-euw";
    private const string KrPuuid = "puuid-new-kr";
    private const string OrphanPuuid = "puuid-orphan-euw";

    public CandidatesApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetCandidates_ReturnsStableShape_OrderedByScoreDesc_WithJoinedRiotId()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/candidates");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["candidates", "total", "page", "pageSize"]);
        root.GetProperty("total").GetInt64().Should().Be(3);
        root.GetProperty("page").GetInt32().Should().Be(1);

        var rows = root.GetProperty("candidates");
        rows.GetArrayLength().Should().Be(3);

        // Row shape is the documented camelCase contract.
        rows[0].EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(
            [
                "id", "platformId", "puuid", "gameName", "tagLine", "championId",
                "championPoints", "championRankInMasteryTop", "score", "status",
                "discoveredAtUtc", "scoredAtUtc", "validatedAtUtc", "lastPlayTimeUtc"
            ]);

        // Ordered by score desc: Validated (9.0) > New KR (5.0) > Orphan (1.0).
        var scores = rows.EnumerateArray().Select(r => r.GetProperty("score").GetDouble()).ToList();
        scores.Should().BeInDescendingOrder();
        rows[0].GetProperty("id").GetGuid().Should().Be(ValidatedCandidateId);

        // The joined Riot ID is present when an account exists, null otherwise.
        var validated = rows.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == ValidatedCandidateId);
        validated.GetProperty("gameName").GetString().Should().Be("Phantasm");
        validated.GetProperty("tagLine").GetString().Should().Be("EUW");
        validated.GetProperty("status").GetString().Should().Be("Validated");

        var orphan = rows.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == OrphanCandidateId);
        orphan.GetProperty("gameName").ValueKind.Should().Be(JsonValueKind.Null);
        orphan.GetProperty("tagLine").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetCandidates_FiltersByStatus()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<CandidatesContract>("/ops/candidates?status=validated");

        payload.Should().NotBeNull();
        payload!.Total.Should().Be(1);
        payload.Candidates.Should().ContainSingle()
            .Which.Id.Should().Be(ValidatedCandidateId);
    }

    [Fact]
    public async Task GetCandidates_FiltersByRegion_CaseInsensitive()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<CandidatesContract>("/ops/candidates?region=kr");

        payload.Should().NotBeNull();
        payload!.Candidates.Should().ContainSingle()
            .Which.PlatformId.Should().Be("KR");
    }

    [Fact]
    public async Task GetCandidates_SearchesByRiotId_PuuidAndChampionId()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        // Riot ID (gameName) — only the Validated candidate's account is "Phantasm".
        var byRiotId = await client.GetFromJsonAsync<CandidatesContract>("/ops/candidates?search=phant");
        byRiotId!.Candidates.Should().ContainSingle().Which.Id.Should().Be(ValidatedCandidateId);

        // PUUID — exact-ish substring on the orphan candidate (no account).
        var byPuuid = await client.GetFromJsonAsync<CandidatesContract>($"/ops/candidates?search={OrphanPuuid}");
        byPuuid!.Candidates.Should().ContainSingle().Which.Id.Should().Be(OrphanCandidateId);

        // Champion id — numeric term matches ChampionId 64 (the KR candidate).
        var byChampion = await client.GetFromJsonAsync<CandidatesContract>("/ops/candidates?search=64");
        byChampion!.Candidates.Should().ContainSingle().Which.Id.Should().Be(NewKrCandidateId);
    }

    [Fact]
    public async Task GetCandidateById_ReturnsDetail_WithLinkedSeedRequestAndIngestedMatchCount()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync($"/ops/candidates/{ValidatedCandidateId}");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(
            [
                "id", "platformId", "puuid", "gameName", "tagLine", "championId",
                "championPoints", "championRankInMasteryTop", "score", "status",
                "discoveredAtUtc", "scoredAtUtc", "validatedAtUtc", "lastPlayTimeUtc",
                "ingestedMatchCount", "seedRequest"
            ]);

        root.GetProperty("gameName").GetString().Should().Be("Phantasm");
        // Two participant rows were seeded for this PUUID.
        root.GetProperty("ingestedMatchCount").GetInt64().Should().Be(2);

        // The manual seed request that resolved to this account is surfaced.
        var seed = root.GetProperty("seedRequest");
        seed.ValueKind.Should().Be(JsonValueKind.Object);
        seed.GetProperty("status").GetString().Should().Be("Ingested");
        seed.GetProperty("resolvedPuuid").GetString().Should().Be(ValidatedPuuid);
    }

    [Fact]
    public async Task GetCandidateById_ReturnsNullSeedRequest_ForOrganicallyDiscoveredCandidate()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync($"/ops/candidates/{NewKrCandidateId}");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("seedRequest").ValueKind.Should().Be(JsonValueKind.Null);
        // No participant rows were seeded for the KR account.
        root.GetProperty("ingestedMatchCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task GetCandidateById_ReturnsNotFound_ForUnknownId()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var response = await client.GetAsync($"/ops/candidates/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CandidateEndpoints_RequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var list = await client.GetAsync("/ops/candidates");
        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var detail = await client.GetAsync($"/ops/candidates/{ValidatedCandidateId}");
        detail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.AddRange(
            BuildAccount(ValidatedPuuid, "Phantasm", "EUW", "EUW1", now),
            BuildAccount(KrPuuid, "Faker", "KR1", "KR", now));

        db.MainCandidates.AddRange(
            new MainCandidate
            {
                Id = ValidatedCandidateId,
                PlatformId = "EUW1",
                Puuid = ValidatedPuuid,
                ChampionId = 7,
                ChampionRankInMasteryTop = 1,
                ChampionPoints = 500_000,
                LastPlayTimeUtc = now.AddDays(-1),
                DiscoveredAtUtc = now.AddDays(-3),
                ScoredAtUtc = now.AddDays(-2),
                ValidatedAtUtc = now.AddDays(-1),
                Score = 9.0,
                Status = MainCandidateStatus.Validated
            },
            new MainCandidate
            {
                Id = NewKrCandidateId,
                PlatformId = "KR",
                Puuid = KrPuuid,
                ChampionId = 64,
                ChampionRankInMasteryTop = 2,
                ChampionPoints = 300_000,
                LastPlayTimeUtc = now.AddDays(-2),
                DiscoveredAtUtc = now.AddDays(-2),
                Score = 5.0,
                Status = MainCandidateStatus.New
            },
            new MainCandidate
            {
                Id = OrphanCandidateId,
                PlatformId = "EUW1",
                Puuid = OrphanPuuid,
                ChampionId = 99,
                ChampionRankInMasteryTop = 3,
                ChampionPoints = 120_000,
                LastPlayTimeUtc = now.AddDays(-5),
                DiscoveredAtUtc = now.AddDays(-1),
                ScoredAtUtc = now.AddHours(-12),
                Score = 1.0,
                Status = MainCandidateStatus.Scored
            });

        // A manual seed request that resolved to the Validated candidate's account.
        db.SeedRequests.Add(new SeedRequest
        {
            Id = Guid.NewGuid(),
            GameName = "Phantasm",
            TagLine = "EUW",
            PlatformId = "EUW1",
            Status = SeedRequestStatus.Ingested,
            RequestedAtUtc = now.AddDays(-3),
            ProcessedAtUtc = now.AddDays(-3).AddMinutes(2),
            ResolvedPuuid = ValidatedPuuid,
            ResolvedRiotAccountId = Guid.NewGuid()
        });

        // Two ingested matches + participant rows for the Validated candidate's
        // PUUID (MatchParticipant has a FK to Match on MatchId, so the parent rows
        // must exist).
        db.Matches.AddRange(
            BuildMatch("EUW1_1", now.AddDays(-1)),
            BuildMatch("EUW1_2", now.AddHours(-12)));
        db.MatchParticipants.AddRange(
            BuildParticipant(ValidatedPuuid, "EUW1_1", 1),
            BuildParticipant(ValidatedPuuid, "EUW1_2", 1));

        await db.SaveChangesAsync();
    }

    private static Match BuildMatch(string id, DateTime gameStart)
        => new()
        {
            Id = id,
            PlatformId = "EUW1",
            QueueId = 420,
            MapId = 11,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStart,
            GameDurationSeconds = 1800,
            GameVersion = "16.4.1",
            CreatedAtUtc = gameStart,
            TimelineIngested = true
        };

    private static RiotAccount BuildAccount(string puuid, string gameName, string tagLine, string platformId, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = now.AddDays(-3),
            UpdatedAtUtc = now
        };

    private static MatchParticipant BuildParticipant(string puuid, string matchId, int participantId)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = puuid,
            SummonerName = "Phantasm",
            SummonerLevel = 100,
            ChampionId = 7,
            TeamId = 100,
            TeamPosition = "TOP",
            IndividualPosition = "TOP",
            Lane = "TOP",
            Role = "SOLO",
            Win = true
        };

    private static HttpClient CreateAuthedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    // Disable the database logging sink in the test host so incidental host
    // warnings never write log rows (kept consistent with the other ops API tests).
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [new KeyValuePair<string, string?>("LoggingSink:Enabled", "false")]);

    private sealed class CandidatesContract
    {
        public List<CandidateRowContract> Candidates { get; init; } = [];

        public long Total { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }
    }

    private sealed class CandidateRowContract
    {
        public Guid Id { get; init; }

        public string PlatformId { get; init; } = string.Empty;

        public string Puuid { get; init; } = string.Empty;

        public string? GameName { get; init; }

        public string? TagLine { get; init; }

        public int ChampionId { get; init; }

        public double Score { get; init; }

        public string Status { get; init; } = string.Empty;
    }
}
