using Core.Lol.Map;
using System.Net;
using System.Text.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class AggregationsApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public AggregationsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAggregationsAsync_ShouldReturnFamiliesRunsAndBacklog()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregationsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/stats/aggregations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("queueId").GetInt32().Should().Be(420);

        var families = root.GetProperty("families").EnumerateArray().ToList();
        families.Select(family => family.GetProperty("key").GetString())
            .Should().BeEquivalentTo(
                ["builds", "matchups", "timelineLeads", "powerspikes", "mains"],
                options => options.WithStrictOrdering());

        // Builds: two scopes on two hotfix versions of the same patch → one
        // normalized patch; no pattern rows seeded.
        var builds = families.Single(family => family.GetProperty("key").GetString() == "builds");
        builds.GetProperty("processName").GetString().Should().Be("ChampionPatternAggregation");
        builds.GetProperty("totalRows").GetInt64().Should().Be(2);
        builds.GetProperty("distinctChampions").GetInt32().Should().Be(2);
        builds.GetProperty("distinctPatches").GetInt32().Should().Be(1);
        builds.GetProperty("lastAggregatedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        builds.GetProperty("tables").EnumerateArray()
            .Select(table => table.GetProperty("table").GetString())
            .Should().BeEquivalentTo(["champion_aggregate_scopes", "champion_aggregate_patterns"]);

        // Matchups: the latest run failed but an older one succeeded — the run
        // rollup must expose both, plus the success run's summary payload.
        var matchups = families.Single(family => family.GetProperty("key").GetString() == "matchups");
        matchups.GetProperty("totalRows").GetInt64().Should().Be(3);
        matchups.GetProperty("distinctChampions").GetInt32().Should().Be(2);
        matchups.GetProperty("distinctPatches").GetInt32().Should().Be(2);
        var matchupsRun = matchups.GetProperty("lastRun");
        matchupsRun.ValueKind.Should().Be(JsonValueKind.Object);
        matchupsRun.GetProperty("status").GetString().Should().Be("failed");
        matchupsRun.GetProperty("lastSuccessAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        matchupsRun.GetProperty("lastSuccessSummary").GetProperty("matchupRows").GetInt32().Should().Be(3);

        // Mains: per-account aggregate, no patch axis.
        var mains = families.Single(family => family.GetProperty("key").GetString() == "mains");
        mains.GetProperty("totalRows").GetInt64().Should().Be(1);
        mains.GetProperty("distinctPatches").ValueKind.Should().Be(JsonValueKind.Null);
        // MainAnalysis never ran in this seed.
        mains.GetProperty("lastRun").ValueKind.Should().Be(JsonValueKind.Null);

        // Backlog: of the three queue-scoped timeline-ingested matches exactly one
        // awaits powerspike folding; the non-soloq match is excluded entirely. One
        // tracked participant misses its elo bracket (the untracked one doesn't count).
        var backlog = root.GetProperty("backlog");
        backlog.GetProperty("timelineIngestedMatches").GetInt64().Should().Be(3);
        backlog.GetProperty("pendingPowerspikeMatches").GetInt64().Should().Be(1);
        backlog.GetProperty("pendingEloBracketParticipants").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task GetAggregationsAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/stats/aggregations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SeedAggregationsAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccount
        {
            Puuid = "aggregations-puuid-1",
            GameName = "AggOne",
            TagLine = "EUW1",
            PlatformId = "EUW1",
            ProfileIconId = 1,
            SummonerLevel = 100
        };
        db.RiotAccounts.Add(account);
        await db.SaveChangesAsync();

        db.ChampionAggregateScopes.AddRange(
            BuildScope(account.Id, championId: 22, gameVersion: "16.4.1", now),
            BuildScope(account.Id, championId: 64, gameVersion: "16.4.2", now));

        db.ChampionMatchupStats.AddRange(
            BuildMatchup(championId: 22, opponent: 64, patch: "16.4", now),
            BuildMatchup(championId: 22, opponent: 99, patch: "16.5", now),
            BuildMatchup(championId: 64, opponent: 22, patch: "16.4", now));

        db.ChampionTimelineLeadStats.Add(new ChampionTimelineLeadStat
        {
            ChampionId = 22,
            TeamPosition = "MIDDLE",
            Patch = "16.4",
            IntervalMinute = 10,
            EloBracket = "GOLD",
            Games = 5,
            TotalGoldDiff = 1500,
            AggregatedAtUtc = now
        });

        db.ChampionPowerspikeCurveStats.Add(new ChampionPowerspikeCurveStat
        {
            ChampionId = 22,
            TeamPosition = "MIDDLE",
            Patch = "16.4",
            EloBracket = "GOLD",
            IntervalMinute = 10,
            Games = 5,
            TotalGoldDiff = 1200,
            TotalDamageDiff = 800,
            AggregatedAtUtc = now
        });

        db.MainChampionStats.Add(new MainChampionStat
        {
            Puuid = account.Puuid,
            PlatformId = "EUW1",
            ChampionId = 22,
            TotalMatches = 20,
            ChampionMatches = 15,
            PlayRate = 0.75,
            IsMain = true,
            IsOtp = false,
            PrimaryPosition = "MIDDLE",
            CalculatedAtUtc = now.AddHours(-1)
        });

        // Soloq: one aggregated, one pending, one pending-but-no-timeline (not in
        // the powerspike backlog). Plus one non-soloq match that never counts.
        db.Matches.AddRange(
            BuildMatch("AGG_1", queueId: 420, timelineIngested: true, powerspikeAggregated: true, now),
            BuildMatch("AGG_2", queueId: 420, timelineIngested: true, powerspikeAggregated: false, now),
            BuildMatch("AGG_3", queueId: 420, timelineIngested: true, powerspikeAggregated: true, now),
            BuildMatch("AGG_4", queueId: 420, timelineIngested: false, powerspikeAggregated: false, now),
            BuildMatch("AGG_5", queueId: 440, timelineIngested: true, powerspikeAggregated: false, now));

        db.MatchParticipants.AddRange(
            BuildParticipant("AGG_1", account.Puuid, account.Id, eloBracket: string.Empty,
                Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001")),
            BuildParticipant("AGG_2", account.Puuid, account.Id, eloBracket: "GOLD",
                Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")),
            BuildParticipant("AGG_3", "untracked-puuid", riotAccountId: null, eloBracket: string.Empty,
                Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003")));

        db.ProcessRuns.AddRange(
            new ProcessRun
            {
                ProcessName = "ChampionMatchupLeadAggregation",
                StartedAtUtc = now.AddHours(-2),
                FinishedAtUtc = now.AddHours(-2).AddMinutes(3),
                DurationMs = 180_000,
                Status = ProcessRunStatus.Success,
                Summary = JsonDocument.Parse("""{"matchupRows":3,"leadRows":1}""")
            },
            new ProcessRun
            {
                ProcessName = "ChampionMatchupLeadAggregation",
                StartedAtUtc = now.AddHours(-1),
                FinishedAtUtc = now.AddHours(-1).AddMinutes(1),
                DurationMs = 60_000,
                Status = ProcessRunStatus.Failed,
                Error = "boom"
            },
            new ProcessRun
            {
                ProcessName = "ChampionPatternAggregation",
                StartedAtUtc = now.AddHours(-2),
                FinishedAtUtc = now.AddHours(-2).AddMinutes(5),
                DurationMs = 300_000,
                Status = ProcessRunStatus.Success
            });

        await db.SaveChangesAsync();
    }

    private static ChampionAggregateScope BuildScope(Guid riotAccountId, int championId, string gameVersion, DateTime now)
        => new()
        {
            RiotAccountId = riotAccountId,
            ChampionId = championId,
            GameVersion = gameVersion,
            PlatformId = "EUW1",
            QueueId = 420,
            Position = "MIDDLE",
            EloBracket = "GOLD",
            Games = 10,
            Wins = 6,
            Kills = 50,
            Deaths = 30,
            Assists = 40,
            LastGameStartTimeUtc = now.AddDays(-1),
            AggregatedAtUtc = now
        };

    private static ChampionMatchupStat BuildMatchup(int championId, int opponent, string patch, DateTime now)
        => new()
        {
            ChampionId = championId,
            TeamPosition = "MIDDLE",
            OpponentChampionId = opponent,
            Patch = patch,
            EloBracket = "GOLD",
            Games = 8,
            Wins = 4,
            AggregatedAtUtc = now
        };

    private static Match BuildMatch(string id, int queueId, bool timelineIngested, bool powerspikeAggregated, DateTime now)
        => new()
        {
            Id = id,
            PlatformId = "EUW1",
            QueueId = queueId,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = now.AddDays(-1),
            GameDurationSeconds = 1800,
            GameVersion = "16.4.1",
            CreatedAtUtc = now.AddDays(-1),
            TimelineIngested = timelineIngested,
            PowerspikeAggregated = powerspikeAggregated
        };

    private static MatchParticipant BuildParticipant(
        string matchId,
        string puuid,
        Guid? riotAccountId,
        string eloBracket,
        Guid id)
        => new()
        {
            Id = id,
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = puuid,
            RiotAccountId = riotAccountId,
            EloBracket = eloBracket,
            SummonerName = puuid,
            SummonerLevel = 100,
            ChampionId = 22,
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

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);
}
