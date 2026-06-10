using System.Net;
using System.Net.Http.Json;
using Core.Lol.Map;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the data-quality panel endpoints. The shared seed builds, on Summoner's
/// Rift, one healthy 10-player match plus the deliberately-broken ones (stuck
/// timeline, short roster, a team missing a lane, zero duration, a duplicate
/// champion on one team, and a half-team match where team 200 has no rows) plus
/// one ARAM match with no lanes — which must NOT be flagged for a missing
/// position, proving the queue-scoping. Other facts seed their own focused data
/// (old-stuck-behind-newer-healthy, unprofiled-queue zero-duration).
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
    public async Task GetIncompleteMatches_FlagsDuplicateChampionOnALaneTeam()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            "/ops/data-quality/incomplete-matches");

        payload.Should().NotBeNull();
        var byIssue = payload!.Groups.ToDictionary(g => g.IssueType);

        // The duplicate-champion match (team 100 has champion 1 twice) is flagged.
        byIssue.Should().ContainKey("duplicateChampion");
        byIssue["duplicateChampion"].Matches.Should().Contain(m => m.MatchId == "DQ_DUP_CHAMP");

        // A healthy match (distinct champions everywhere) must NOT be flagged for it.
        byIssue["duplicateChampion"].Matches
            .Should().NotContain(m => m.MatchId == "DQ_HEALTHY");
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
    public async Task GetIncompleteMatches_FindsOldStuckTimeline_BehindManyNewerHealthyMatches()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            // Many newer, fully-healthy SR matches (timeline ingested, complete
            // roster, valid duration) ordered ahead of the stuck one.
            for (var i = 0; i < 50; i++)
            {
                var id = $"DQ_NEW_HEALTHY_{i:D3}";
                db.Matches.Add(BuildMatch(id, LolQueueId.RankedSoloDuo, now.AddHours(-i - 1), 1800, timelineIngested: true));
                AddFullSrRoster(db, id);
            }

            // One OLD match whose timeline never ingested — well past the staleness
            // window and older than every healthy match above. The newest-first
            // candidate window must still surface it (the predicate is applied in
            // the DB before any cap), not bury it behind the newer healthy ones.
            db.Matches.Add(BuildMatch("DQ_OLD_STUCK", LolQueueId.RankedSoloDuo, now.AddDays(-30), 1800, timelineIngested: false));
            AddFullSrRoster(db, "DQ_OLD_STUCK");

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            "/ops/data-quality/incomplete-matches");

        payload.Should().NotBeNull();
        var byIssue = payload!.Groups.ToDictionary(g => g.IssueType);

        byIssue.Should().ContainKey("missingTimeline");
        byIssue["missingTimeline"].Matches.Should().Contain(m => m.MatchId == "DQ_OLD_STUCK");

        // The newer healthy matches must not be flagged at all.
        payload.Groups.SelectMany(g => g.Matches)
            .Should().NotContain(m => m.MatchId.StartsWith("DQ_NEW_HEALTHY"));
    }

    [Fact]
    public async Task GetIncompleteMatches_FlagsZeroDuration_OnAnUnprofiledQueue()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            // A queue id with NO data-quality profile. The queue-agnostic checks
            // (zero-duration here) must still flag it, even though the count/
            // position rules don't apply to an unknown queue.
            const int unprofiledQueueId = 1700; // Arena — not in the profile table.
            db.Matches.Add(new Match
            {
                Id = "DQ_UNKNOWN_QUEUE",
                PlatformId = "EUW1",
                QueueId = unprofiledQueueId,
                MapId = (int)LolMapId.SummonersRift,
                GameMode = "CHERRY",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddDays(-1),
                GameDurationSeconds = 0,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddDays(-1),
                TimelineIngested = true
            });
            db.MatchParticipants.Add(BuildParticipant("DQ_UNKNOWN_QUEUE", 1, 100, "", championId: 1));

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            "/ops/data-quality/incomplete-matches");

        payload.Should().NotBeNull();
        var byIssue = payload!.Groups.ToDictionary(g => g.IssueType);

        byIssue.Should().ContainKey("zeroDuration");
        byIssue["zeroDuration"].Matches.Should().Contain(m => m.MatchId == "DQ_UNKNOWN_QUEUE");

        // The unknown queue carries no count/position profile, so it must NOT be
        // flagged for wrong-participant-count just because it has one row.
        if (byIssue.TryGetValue("wrongParticipantCount", out var wrongCount))
        {
            wrongCount.Matches.Should().NotContain(m => m.MatchId == "DQ_UNKNOWN_QUEUE");
        }
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
    public async Task GetIncompleteMatches_FiltersByMinAgeHours_ExcludingMatchesYoungerThanTheFloor()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            // Two zero-duration SR matches straddling a 24h age floor: one played
            // 2h ago (younger than the floor) and one played 48h ago (older).
            db.Matches.Add(BuildMatch("DQ_YOUNG", LolQueueId.RankedSoloDuo, now.AddHours(-2), 0, timelineIngested: true));
            AddFullSrRoster(db, "DQ_YOUNG");

            db.Matches.Add(BuildMatch("DQ_OLD", LolQueueId.RankedSoloDuo, now.AddHours(-48), 0, timelineIngested: true));
            AddFullSrRoster(db, "DQ_OLD");

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            "/ops/data-quality/incomplete-matches?minAgeHours=24");

        payload.Should().NotBeNull();
        var flaggedIds = payload!.Groups.SelectMany(g => g.Matches).Select(m => m.MatchId).ToList();

        // Only the match older than the 24h floor survives; the 2h-old one is gone.
        flaggedIds.Should().Contain("DQ_OLD");
        flaggedIds.Should().NotContain("DQ_YOUNG");
    }

    [Fact]
    public async Task GetIncompleteMatches_PaginatesEachGroup_ReturningTheExpectedSecondPageSlice()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        // Five zero-duration SR matches with strictly descending start times so the
        // newest-first order is deterministic: DQ_PAGE_0 newest … DQ_PAGE_4 oldest.
        var expectedNewestFirst = new List<string>();
        await using (var db = _fixture.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                var id = $"DQ_PAGE_{i}";
                expectedNewestFirst.Add(id);
                db.Matches.Add(BuildMatch(id, LolQueueId.RankedSoloDuo, now.AddHours(-i - 1), 0, timelineIngested: true));
                AddFullSrRoster(db, id);
            }

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        // pageSize=2 over 5 matches → page 2 is the third+fourth newest. Filter to
        // the single zero-duration group so the slice is unambiguous.
        var page2 = await client.GetFromJsonAsync<IncompleteMatchesContract>(
            "/ops/data-quality/incomplete-matches?issue=zeroDuration&page=2&pageSize=2");

        page2.Should().NotBeNull();
        var group = page2!.Groups.Single(g => g.IssueType == "zeroDuration");
        // Full count is reported independent of the page.
        group.Count.Should().Be(5);

        var page2Ids = group.Matches.Select(m => m.MatchId).ToList();
        // The Skip/Take offset must yield the 3rd and 4th newest, not page 1's slice
        // and not a duplicated/empty page.
        page2Ids.Should().Equal(expectedNewestFirst[2], expectedNewestFirst[3]);
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
    public async Task GetMatchDetail_RendersBothStandardTeams_WhenOneTeamHasNoRows()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var detail = await client.GetFromJsonAsync<MatchDetailContract>(
            "/ops/data-quality/match/DQ_HALF_TEAM");

        detail.Should().NotBeNull();
        detail!.HasLanes.Should().BeTrue();
        // Both standard teams are present even though team 200 has zero rows.
        detail.Teams.Should().HaveCount(2);
        detail.Teams.Select(t => t.TeamId).Should().BeEquivalentTo(new[] { 100, 200 });

        // Team 200 is fully absent: five canonical lane slots, all unfilled.
        var team200 = detail.Teams.Single(t => t.TeamId == 200);
        team200.Slots.Should().HaveCount(5);
        team200.Slots.Should().OnlyContain(s => !s.Filled);
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

        // 7) Duplicate champion: 10 players across the five lanes per team, but
        // team 100's TOP and JUNGLE share champion id 1 (impossible in a real
        // game — a duplicated/garbled participant row). Lane queue, so the
        // duplicate-champion check applies.
        db.Matches.Add(BuildMatch("DQ_DUP_CHAMP", LolQueueId.RankedSoloDuo, now.AddDays(-7), 1800, timelineIngested: true));
        AddSrRosterWithChampions(db, "DQ_DUP_CHAMP",
            team100: [("TOP", 1), ("JUNGLE", 1), ("MIDDLE", 3), ("BOTTOM", 4), ("UTILITY", 5)],
            team200: [("TOP", 6), ("JUNGLE", 7), ("MIDDLE", 8), ("BOTTOM", 9), ("UTILITY", 10)]);

        // 8) Half-team: a standard SR match where team 200 has NO ingested rows at
        // all (only team 100 is present). The detail must still render team 200
        // with its five missing lane slots, not omit the team entirely.
        db.Matches.Add(BuildMatch("DQ_HALF_TEAM", LolQueueId.RankedSoloDuo, now.AddDays(-8), 1800, timelineIngested: true));
        AddSrRoster(db, "DQ_HALF_TEAM",
            team100Positions: ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"],
            team200Positions: []);

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

    // Like AddSrRoster but with explicit champion ids per slot, so a roster can
    // deliberately repeat a champion on one team (duplicate-champion check).
    private static void AddSrRosterWithChampions(
        Data.TrueMainDbContext db,
        string matchId,
        IReadOnlyList<(string Position, int ChampionId)> team100,
        IReadOnlyList<(string Position, int ChampionId)> team200)
    {
        var participantId = 1;
        foreach (var (position, championId) in team100)
        {
            db.MatchParticipants.Add(BuildParticipant(matchId, participantId++, 100, position, championId));
        }

        foreach (var (position, championId) in team200)
        {
            db.MatchParticipants.Add(BuildParticipant(matchId, participantId++, 200, position, championId));
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
