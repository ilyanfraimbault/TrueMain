using System.Net;
using System.Net.Http.Json;
using Core.Lol.Map;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the data-quality panel endpoints. The seed builds, on Summoner's Rift,
/// one healthy 10-player match plus four deliberately-broken ones (stuck
/// timeline, short roster, a team missing a lane, zero duration) and one ARAM
/// match with no lanes — which must NOT be flagged for a missing position, proving
/// the queue-scoping.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DataQualityApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public DataQualityApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetIncompleteMatches_GroupsFlaggedMatchesByIssue_AndScopesByQueue()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            "/ops/data-quality/incomplete-matches");

        payload.Should().NotBeNull();
        payload!.StaleTimelineThresholdHours.Should().BeGreaterThan(0);

        var byIssue = payload.Groups.ToDictionary(g => g.IssueType);

        // The healthy SR match (DQ_HEALTHY) is in no group.
        payload.Groups.SelectMany(g => g.Matches)
            .Should().NotContain(m => m.MatchId == "DQ_HEALTHY");

        byIssue.Should().ContainKey("missingTimeline");
        byIssue["missingTimeline"].Matches.Should().Contain(m => m.MatchId == "DQ_STALE_TL");

        byIssue.Should().ContainKey("wrongParticipantCount");
        byIssue["wrongParticipantCount"].Matches.Should().Contain(m => m.MatchId == "DQ_SHORT");

        byIssue.Should().ContainKey("missingTeamPosition");
        byIssue["missingTeamPosition"].Matches.Should().Contain(m => m.MatchId == "DQ_NO_LANE");

        byIssue.Should().ContainKey("zeroDuration");
        byIssue["zeroDuration"].Matches.Should().Contain(m => m.MatchId == "DQ_ZERO_DUR");

        // ARAM has no lanes, so a 10-player ARAM match with no TeamPosition must
        // NOT show up under missingTeamPosition (queue-scoping).
        payload.Groups
            .Where(g => g.IssueType == "missingTeamPosition")
            .SelectMany(g => g.Matches)
            .Should().NotContain(m => m.MatchId == "DQ_ARAM");
    }

    [Fact]
    public async Task GetIncompleteMatches_FiltersToASingleIssueType()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            "/ops/data-quality/incomplete-matches?issue=zeroDuration");

        payload.Should().NotBeNull();
        payload!.Groups.Should().OnlyContain(g => g.IssueType == "zeroDuration");
        payload.Groups.Should().ContainSingle();
        payload.Groups[0].Matches.Should().Contain(m => m.MatchId == "DQ_ZERO_DUR");
    }

    [Fact]
    public async Task GetIncompleteMatches_FiltersByQueue()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            $"/ops/data-quality/incomplete-matches?queue={(int)LolQueueId.Aram}");

        payload.Should().NotBeNull();
        // Only ARAM matches are in scope; the SR broken ones must be absent.
        payload!.Groups.SelectMany(g => g.Matches)
            .Should().OnlyContain(m => m.QueueId == (int)LolQueueId.Aram);
    }

    [Fact]
    public async Task GetMatchDetail_LaysOutTeamsByPosition_AndHighlightsTheGap()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var detail = await client.GetFromJsonAsync<MatchDetailContract>(
            "/ops/data-quality/match/DQ_NO_LANE");

        detail.Should().NotBeNull();
        detail!.QueueKnown.Should().BeTrue();
        detail.HasLanes.Should().BeTrue();
        detail.Issues.Should().Contain("missingTeamPosition");
        detail.Teams.Should().HaveCount(2);

        // Team 100 has two MIDDLEs and no UTILITY, so the canonical UTILITY slot
        // is present-but-unfilled (the highlighted gap), and the extra MIDDLE
        // participant is surfaced as an additional slot rather than dropped.
        var team100 = detail.Teams.Single(t => t.TeamId == 100);
        var utilitySlots = team100.Slots.Where(s => s.Position == "UTILITY").ToList();
        utilitySlots.Should().ContainSingle();
        utilitySlots[0].Filled.Should().BeFalse();
        // The five canonical lanes are always present; the duplicate MIDDLE adds a 6th.
        team100.Slots.Should().HaveCount(6);
        team100.Slots.Count(s => s.Filled).Should().Be(5);
    }

    [Fact]
    public async Task GetMatchDetail_ReturnsNotFound_ForUnknownMatch()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var response = await client.GetAsync("/ops/data-quality/match/DOES_NOT_EXIST");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DataQualityEndpoints_RequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var list = await client.GetAsync("/ops/data-quality/incomplete-matches");
        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var detail = await client.GetAsync("/ops/data-quality/match/DQ_HEALTHY");
        detail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpClient CreateAuthedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        // 1) Healthy SR match: 10 players, all 5 lanes per team, timeline ingested.
        db.Matches.Add(BuildMatch("DQ_HEALTHY", LolQueueId.RankedSoloDuo, now.AddDays(-1), 1800, timelineIngested: true));
        AddFullSrRoster(db, "DQ_HEALTHY");

        // 2) Stuck timeline: complete roster, but TimelineIngested=false and old
        // enough to be past the staleness window.
        db.Matches.Add(BuildMatch("DQ_STALE_TL", LolQueueId.RankedSoloDuo, now.AddDays(-2), 1800, timelineIngested: false));
        AddFullSrRoster(db, "DQ_STALE_TL");

        // 3) Wrong participant count: only 8 of the expected 10 players.
        db.Matches.Add(BuildMatch("DQ_SHORT", LolQueueId.RankedSoloDuo, now.AddDays(-3), 1800, timelineIngested: true));
        AddSrRoster(db, "DQ_SHORT", team100Positions: ["TOP", "JUNGLE", "MIDDLE", "BOTTOM"], team200Positions: ["TOP", "JUNGLE", "MIDDLE", "BOTTOM"]);

        // 4) Missing team position: 10 players but team 100 has two MIDDLEs and no UTILITY.
        db.Matches.Add(BuildMatch("DQ_NO_LANE", LolQueueId.RankedSoloDuo, now.AddDays(-4), 1800, timelineIngested: true));
        AddSrRoster(db, "DQ_NO_LANE",
            team100Positions: ["TOP", "JUNGLE", "MIDDLE", "MIDDLE", "BOTTOM"],
            team200Positions: ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"]);

        // 5) Zero duration: otherwise-complete SR match with GameDurationSeconds = 0.
        db.Matches.Add(BuildMatch("DQ_ZERO_DUR", LolQueueId.RankedSoloDuo, now.AddDays(-5), 0, timelineIngested: true));
        AddFullSrRoster(db, "DQ_ZERO_DUR");

        // 6) ARAM: 10 players, NO lane data. Must NOT be flagged for missing position.
        db.Matches.Add(BuildMatch("DQ_ARAM", LolQueueId.Aram, now.AddDays(-6), 1200, timelineIngested: true));
        for (var i = 1; i <= 10; i++)
        {
            db.MatchParticipants.Add(BuildParticipant(
                "DQ_ARAM", i, teamId: i <= 5 ? 100 : 200, position: "", championId: 100 + i));
        }

        await db.SaveChangesAsync();
    }

    private static void AddFullSrRoster(Data.TrueMainDbContext db, string matchId)
        => AddSrRoster(db, matchId,
            team100Positions: ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"],
            team200Positions: ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"]);

    private static void AddSrRoster(
        Data.TrueMainDbContext db,
        string matchId,
        IReadOnlyList<string> team100Positions,
        IReadOnlyList<string> team200Positions)
    {
        var participantId = 1;
        var championId = 1;
        foreach (var position in team100Positions)
        {
            db.MatchParticipants.Add(BuildParticipant(matchId, participantId++, 100, position, championId++));
        }

        foreach (var position in team200Positions)
        {
            db.MatchParticipants.Add(BuildParticipant(matchId, participantId++, 200, position, championId++));
        }
    }

    private static Match BuildMatch(
        string id,
        LolQueueId queueId,
        DateTime gameStart,
        int durationSeconds,
        bool timelineIngested) => new()
        {
            Id = id,
            PlatformId = "EUW1",
            QueueId = (int)queueId,
            MapId = queueId == LolQueueId.Aram ? (int)LolMapId.HowlingAbyss : (int)LolMapId.SummonersRift,
            GameMode = queueId == LolQueueId.Aram ? "ARAM" : "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStart,
            GameDurationSeconds = durationSeconds,
            GameVersion = "16.4.1",
            CreatedAtUtc = gameStart,
            TimelineIngested = timelineIngested
        };

    private static MatchParticipant BuildParticipant(
        string matchId,
        int participantId,
        int teamId,
        string position,
        int championId) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"{matchId}-p{participantId}",
            SummonerName = $"{matchId}-p{participantId}",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = position,
            IndividualPosition = position,
            Lane = position,
            Role = "NONE",
            Win = teamId == 100,
            ItemEvents = [],
            SkillEvents = []
        };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class IncompleteMatchesContract
    {
        public IReadOnlyList<IssueGroupContract> Groups { get; init; } = [];

        public long Total { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }

        public int StaleTimelineThresholdHours { get; init; }
    }

    private sealed class IssueGroupContract
    {
        public string IssueType { get; init; } = string.Empty;

        public long Count { get; init; }

        public IReadOnlyList<FlaggedMatchContract> Matches { get; init; } = [];
    }

    private sealed class FlaggedMatchContract
    {
        public string MatchId { get; init; } = string.Empty;

        public int QueueId { get; init; }

        public IReadOnlyList<string> Issues { get; init; } = [];
    }

    private sealed class MatchDetailContract
    {
        public string MatchId { get; init; } = string.Empty;

        public bool QueueKnown { get; init; }

        public bool HasLanes { get; init; }

        public IReadOnlyList<string> Issues { get; init; } = [];

        public IReadOnlyList<TeamContract> Teams { get; init; } = [];
    }

    private sealed class TeamContract
    {
        public int TeamId { get; init; }

        public IReadOnlyList<SlotContract> Slots { get; init; } = [];
    }

    private sealed class SlotContract
    {
        public string Position { get; init; } = string.Empty;

        public bool Filled { get; init; }

        public int? ChampionId { get; init; }
    }
}
