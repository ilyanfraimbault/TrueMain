using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the admin account explorer (<c>GET /ops/accounts/{nameTag}</c>, #1032).
/// The endpoint's whole point is that every distinct pipeline state renders a
/// distinct, self-explanatory answer, so each fact seeds exactly one state:
/// tracked (an active main), candidate-only (no main row at all), retired (every
/// main row deactivated), invalidated (a dead PUUID), and unknown (nothing in the
/// database at all). A separate fact covers the "retention pruned this account's
/// games" detection, which is the one piece of business logic dense enough to
/// deserve its own seed.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AccountExplorerApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    private const string TrackedPuuid = "puuid-tracked-euw";
    private const string CandidateOnlyPuuid = "puuid-candidate-only-euw";
    private const string RetiredPuuid = "puuid-retired-euw";
    private const string InvalidPuuid = "puuid-invalid-euw";
    private const string PrunedPuuid = "puuid-pruned-euw";

    public AccountExplorerApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    [Fact]
    public async Task GetAccountExplorer_TrackedAccount_ReturnsTrackedStateWithActiveMain()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var account = BuildAccount(TrackedPuuid, "Phantasm", "EUW", "EUW1", now);
            db.RiotAccounts.Add(account);
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "EUW1",
                Puuid = TrackedPuuid,
                ChampionId = 7,
                TotalMatches = 40,
                ChampionMatches = 32,
                PlayRate = 0.8,
                IsMain = true,
                IsActive = true,
                IsOtp = false,
                PrimaryPosition = "MIDDLE",
                CalculatedAtUtc = now.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/Phantasm-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("Tracked");
        root.GetProperty("identity").GetProperty("puuid").GetString().Should().Be(TrackedPuuid);
        root.GetProperty("tracking").GetProperty("isTracked").GetBoolean().Should().BeTrue();
        root.GetProperty("tracking").GetProperty("trackedVia").GetString().Should().Be("EstablishedMain");
        root.GetProperty("mains").GetProperty("rows").GetArrayLength().Should().Be(1);
        root.GetProperty("mains").GetProperty("rows")[0].GetProperty("isActive").GetBoolean().Should().BeTrue();
        root.GetProperty("mains").GetProperty("rows")[0].GetProperty("deactivation").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetAccountExplorer_CandidateOnly_ReturnsCandidateOnlyState()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(BuildAccount(CandidateOnlyPuuid, "Faker", "KR1", "KR", now));
            db.MainCandidates.Add(new MainCandidate
            {
                Id = Guid.NewGuid(),
                PlatformId = "KR",
                Puuid = CandidateOnlyPuuid,
                ChampionId = 64,
                ChampionRankInMasteryTop = 1,
                ChampionPoints = 250_000,
                LastPlayTimeUtc = now.AddDays(-1),
                DiscoveredAtUtc = now.AddDays(-2),
                ScoredAtUtc = now.AddDays(-1),
                Score = 6.5,
                Status = MainCandidateStatus.Scored
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/Faker%23KR1?region=KR");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("CandidateOnly");
        root.GetProperty("candidates").GetArrayLength().Should().Be(1);
        root.GetProperty("candidates")[0].GetProperty("status").GetString().Should().Be("Scored");
        root.GetProperty("candidates")[0].GetProperty("scoreInputs").GetProperty("championRankInMasteryTop")
            .GetInt32().Should().Be(1);
        root.GetProperty("mains").GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetAccountExplorer_AllMainsDeactivated_ReturnsRetiredStateWithDeactivationNote()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var account = BuildAccount(RetiredPuuid, "OldMain", "EUW", "EUW1", now);
            account.LastActivityCheckAtUtc = now.AddHours(-2);
            db.RiotAccounts.Add(account);
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "EUW1",
                Puuid = RetiredPuuid,
                ChampionId = 99,
                TotalMatches = 40,
                ChampionMatches = 35,
                PlayRate = 0.875,
                IsMain = true,
                IsActive = false,
                IsOtp = true,
                PrimaryPosition = "JUNGLE",
                CalculatedAtUtc = now.AddDays(-60)
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/OldMain-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("Retired");
        var row = root.GetProperty("mains").GetProperty("rows")[0];
        row.GetProperty("isActive").GetBoolean().Should().BeFalse();
        var deactivation = row.GetProperty("deactivation");
        deactivation.ValueKind.Should().NotBe(JsonValueKind.Null);
        deactivation.GetProperty("reasonKnown").GetBoolean().Should().BeFalse();
        deactivation.GetProperty("confirmedByActivityCheckAtUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetAccountExplorer_InvalidAccount_ReturnsInvalidatedState()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var account = BuildAccount(InvalidPuuid, "Ghost", "EUW", "EUW1", now);
            account.Status = RiotAccountStatus.Invalid;
            db.RiotAccounts.Add(account);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/Ghost-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("Invalidated");
        root.GetProperty("identity").GetProperty("status").GetString().Should().Be("Invalid");
    }

    [Fact]
    public async Task GetAccountExplorer_InvalidatedAccountWithStaleActiveMain_ReportsNotTracked()
    {
        // Regression: an account can be invalidated after MainAnalysis already
        // wrote an IsMain && IsActive row for it. The real ingest claim
        // (ClaimAccountsForMatchIngestAtomicallyAsync) gates on
        // RiotAccountStatus.Active before either membership arm matters, so this
        // account is never actually selected for ingestion — tracking.isTracked
        // must agree with that, or the page contradicts its own state banner.
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;
        const string puuid = "puuid-invalid-with-stale-main-euw";

        await using (var db = _fixture.CreateDbContext())
        {
            var account = BuildAccount(puuid, "StaleGhost", "EUW", "EUW1", now);
            account.Status = RiotAccountStatus.Invalid;
            db.RiotAccounts.Add(account);
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "EUW1",
                Puuid = puuid,
                ChampionId = 7,
                TotalMatches = 40,
                ChampionMatches = 32,
                PlayRate = 0.8,
                IsMain = true,
                IsActive = true,
                PrimaryPosition = "MIDDLE",
                CalculatedAtUtc = now.AddDays(-10)
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/StaleGhost-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("Invalidated");
        var tracking = root.GetProperty("tracking");
        tracking.GetProperty("isTracked").GetBoolean().Should().BeFalse();
        tracking.GetProperty("trackedVia").ValueKind.Should().Be(JsonValueKind.Null);
        // The raw structural fact stays visible even though it no longer makes
        // the account eligible — losing it would hide exactly the stale-row
        // situation this test exists to catch.
        tracking.GetProperty("hasActiveMain").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetAccountExplorer_NoAccountButSeedRequestExists_ReturnsSeedRequestedOnlyState()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await _mongo.GetCollection<SeedRequestDocument>(MongoFixture.SeedRequestsCollection)
            .InsertOneAsync(new SeedRequestDocument
            {
                Id = Guid.NewGuid(),
                GameName = "NotYetResolved",
                TagLine = "EUW",
                PlatformId = "EUW1",
                Status = SeedRequestStatus.Failed,
                Error = "account-v1 returned 404",
                RequestedAtUtc = now.AddHours(-3),
                ProcessedAtUtc = now.AddHours(-2)
            });

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/NotYetResolved-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("SeedRequestedOnly");
        root.GetProperty("identity").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("seedRequest").GetProperty("status").GetString().Should().Be("Failed");
        root.GetProperty("stateDetail").GetString().Should().Contain("Failed");
    }

    [Fact]
    public async Task GetAccountExplorer_AnalysedButNoMainCleared_ReturnsNotAMainState()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;
        const string puuid = "puuid-not-a-main-euw";

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(BuildAccount(puuid, "Casual", "EUW", "EUW1", now));
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "EUW1",
                Puuid = puuid,
                ChampionId = 42,
                TotalMatches = 40,
                ChampionMatches = 3,
                PlayRate = 0.075,
                IsMain = false,
                IsActive = true,
                PrimaryPosition = "SUPPORT",
                CalculatedAtUtc = now.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/Casual-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("NotAMain");
        root.GetProperty("mains").GetProperty("rows").GetArrayLength().Should().Be(1);
        root.GetProperty("mains").GetProperty("rows")[0].GetProperty("isMain").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetAccountExplorer_AccountWithNothingElse_ReturnsDiscoveredState()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(BuildAccount("puuid-discovered-only-euw", "JustHere", "EUW", "EUW1", now));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/JustHere-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("Discovered");
        root.GetProperty("candidates").GetArrayLength().Should().Be(0);
        root.GetProperty("mains").GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetAccountExplorer_SameRiotIdAcrossRegions_ListsOtherAccounts()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            // Same (GameName, TagLine) recycled across two regions — the index is
            // deliberately non-unique. The more recently active row (EUW1) must
            // resolve first; the older one (NA1) shows up as a collision, not a
            // silently arbitrated duplicate.
            var euw = BuildAccount("puuid-collision-euw", "Recycled", "EUW", "EUW1", now);
            euw.LastMatchIngestAtUtc = now.AddMinutes(-5);
            var na = BuildAccount("puuid-collision-na", "Recycled", "EUW", "NA1", now.AddDays(-90));
            na.LastMatchIngestAtUtc = now.AddDays(-60);

            db.RiotAccounts.AddRange(euw, na);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/Recycled-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("identity").GetProperty("platformId").GetString().Should().Be("EUW1");
        var others = root.GetProperty("otherAccountsWithSameRiotId");
        others.GetArrayLength().Should().Be(1);
        others[0].GetProperty("platformId").GetString().Should().Be("NA1");
    }

    [Fact]
    public async Task GetAccountExplorer_UnknownRiotId_Returns200WithNeverDiscoveredState()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var response = await client.GetAsync("/ops/accounts/NoOneHere-9999");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("state").GetString().Should().Be("NeverDiscovered");
        root.GetProperty("identity").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("stateDetail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetAccountExplorer_AggregatesOutliveParticipants_ReportsPruned()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var account = BuildAccount(PrunedPuuid, "Pruned", "EUW", "EUW1", now);
            db.RiotAccounts.Add(account);

            // A frozen aggregate reaching back further than any surviving
            // participant row — the one signal the endpoint can use to prove
            // retention deleted something, since the aggregates only ever folded
            // main champions.
            db.ChampionAggregateScopes.Add(new ChampionAggregateScope
            {
                Id = Guid.NewGuid(),
                RiotAccountId = account.Id,
                ChampionId = 7,
                GameVersion = "15.1.1",
                PlatformId = "EUW1",
                QueueId = 420,
                Position = "MIDDLE",
                EloBracket = "GOLD",
                Games = 20,
                Wins = 11,
                LastGameStartTimeUtc = now.AddMonths(-6),
                AggregatedAtUtc = now.AddMonths(-6)
            });

            // A single surviving participant row, much more recent than the
            // aggregate's history — retention has kept only the current window.
            db.Matches.Add(new Match
            {
                Id = "EUW1_PRUNED_1",
                PlatformId = "EUW1",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddDays(-2),
                GameDurationSeconds = 1800,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddDays(-2),
                TimelineIngested = true
            });
            db.MatchParticipants.Add(new MatchParticipant
            {
                Id = Guid.NewGuid(),
                MatchId = "EUW1_PRUNED_1",
                ParticipantId = 1,
                Puuid = PrunedPuuid,
                SummonerName = "Pruned",
                SummonerLevel = 100,
                ChampionId = 7,
                TeamId = 100,
                TeamPosition = "MIDDLE",
                IndividualPosition = "MIDDLE",
                Lane = "MIDDLE",
                Role = "SOLO",
                Win = true
            });

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var json = await client.GetStringAsync("/ops/accounts/Pruned-EUW");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var matches = root.GetProperty("matchesIngested");
        matches.GetProperty("liveParticipantCount").GetInt64().Should().Be(1);
        matches.GetProperty("careerGamesFromAggregates").GetInt64().Should().Be(20);
        matches.GetProperty("pruned").GetBoolean().Should().BeTrue();
        matches.GetProperty("prunedNote").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetAccountExplorer_MalformedRiotId_Returns400()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        // No '#' and no '-' — cannot be split into a game name and a tag.
        var response = await client.GetAsync("/ops/accounts/NotARiotId");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAccountExplorer_UnknownRegion_Returns400()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var response = await client.GetAsync("/ops/accounts/Phantasm-EUW?region=NOT_A_PLATFORM");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AccountsSeedLiteralRoute_StillResolvesToSeedList_NotAccountExplorer()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        // "seed" would parse as a one-segment nameTag if the literal route lost
        // to {nameTag} in routing precedence — assert it still lands on the
        // seed-request list endpoint (an array), not the explorer (an object).
        var response = await client.GetAsync("/ops/accounts/seed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetAccountExplorer_RequiresOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/accounts/Phantasm-EUW");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static RiotAccount BuildAccount(
        string puuid, string gameName, string tagLine, string platformId, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = now.AddDays(-30),
            UpdatedAtUtc = now,
            LastMatchIngestAtUtc = now.AddHours(-1)
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
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);
}
