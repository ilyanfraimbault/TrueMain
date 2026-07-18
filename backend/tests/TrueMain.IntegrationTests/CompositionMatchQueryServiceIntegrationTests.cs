using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using TrueMain.Options;
using TrueMain.Services.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Service-level coverage of the composition match search (#563): hard filter
/// on champion+position over the full pool (harvested rows included),
/// similarity-ranked selection, and the graceful degradation to plain recency
/// when the requested draft matches nothing.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CompositionMatchQueryServiceIntegrationTests
{
    private const int Champion = 157; // Yone
    private const int LaneOpponent = 238; // Zed
    private const int OtherOpponent = 91; // Talon
    private const int EnemyTop = 266; // Aatrox
    private const int AllyJungle = 64; // Lee Sin
    private const string Position = "MIDDLE";

    private readonly PostgresFixture _fixture;

    public CompositionMatchQueryServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindTopMatchesAsync_RanksByCompositionSimilarity()
    {
        await _fixture.ResetDatabaseAsync();

        // Three games, from most to least similar to the requested draft
        // (vs Zed MIDDLE + Aatrox TOP, with Lee Sin JUNGLE):
        //   full-hit   — lane opponent + enemy top + ally jungle = 10+4+2 = 16
        //   lane-only  — lane opponent only                       = 10
        //   unrelated  — Talon mid: no matchup, hard-filtered out
        // The unrelated game is the most recent: with the lane opponent
        // pinned, the matchup requirement must drop it entirely instead of
        // merely out-scoring it.
        await SeedGameAsync("COMP_FULLHIT", daysAgo: 3, win: true, enemyMid: LaneOpponent, enemyTop: EnemyTop, allyJungle: AllyJungle);
        await SeedGameAsync("COMP_LANEONLY", daysAgo: 2, win: false, enemyMid: LaneOpponent);
        await SeedGameAsync("COMP_UNRELATED", daysAgo: 1, win: true, enemyMid: OtherOpponent);

        await using var db = _fixture.CreateDbContext();
        var result = await CreateService(db).FindTopMatchesAsync(
            new CompositionSearchCriteria
            {
                ChampionId = Champion,
                Position = Position,
                Enemies = new Dictionary<string, int> { ["MIDDLE"] = LaneOpponent, ["TOP"] = EnemyTop },
                Allies = new Dictionary<string, int> { ["JUNGLE"] = AllyJungle },
            },
            CancellationToken.None);

        result.CandidatePoolSize.Should().Be(3);
        result.MaxPossibleScore.Should().Be(16);
        result.MatchupRequested.Should().BeTrue();
        result.MatchupFound.Should().BeTrue();
        result.Matches.Select(m => m.MatchId).Should().Equal("COMP_FULLHIT", "COMP_LANEONLY");
        result.Matches.Select(m => m.Score).Should().Equal(16, 10);
        result.Matches[0].ParticipantId.Should().Be(1, "the selected row is the searched champion");
        result.Matches[0].Win.Should().BeTrue();
        result.MeanSimilarity.Should().BeApproximately((16d / 16 + 10d / 16) / 2, 1e-9);
    }

    [Fact]
    public async Task FindTopMatchesAsync_NoGameWithTheRequestedMatchup_ReturnsEmptyWithTheFlag()
    {
        await _fixture.ResetDatabaseAsync();

        await SeedGameAsync("COMP_OTHERMID", daysAgo: 1, win: true, enemyMid: OtherOpponent);

        await using var db = _fixture.CreateDbContext();
        var result = await CreateService(db).FindTopMatchesAsync(
            new CompositionSearchCriteria
            {
                ChampionId = Champion,
                Position = Position,
                Enemies = new Dictionary<string, int> { ["MIDDLE"] = LaneOpponent },
            },
            CancellationToken.None);

        // The pool was scanned but nothing has the matchup: the caller falls
        // back to the champion's baseline build and says so.
        result.CandidatePoolSize.Should().Be(1);
        result.MatchupRequested.Should().BeTrue();
        result.MatchupFound.Should().BeFalse();
        result.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task FindTopMatchesAsync_SearchesTheFullPool_HarvestedRowsIncluded()
    {
        await _fixture.ResetDatabaseAsync();

        // Every seeded participant is untracked (RiotAccountId = null) — the
        // tracked-only partial index population would be empty here.
        await SeedGameAsync("COMP_HARVESTED", daysAgo: 1, win: true, enemyMid: LaneOpponent);

        await using var db = _fixture.CreateDbContext();
        var result = await CreateService(db).FindTopMatchesAsync(
            new CompositionSearchCriteria
            {
                ChampionId = Champion,
                Position = Position,
                Enemies = new Dictionary<string, int> { ["MIDDLE"] = LaneOpponent },
            },
            CancellationToken.None);

        result.CandidatePoolSize.Should().Be(1);
        result.Matches.Should().ContainSingle().Which.Score.Should().Be(10);
    }

    [Fact]
    public async Task FindTopMatchesAsync_EmptyDraft_FallsBackToRecency()
    {
        await _fixture.ResetDatabaseAsync();

        await SeedGameAsync("COMP_OLDER", daysAgo: 5, win: true, enemyMid: LaneOpponent);
        await SeedGameAsync("COMP_NEWER", daysAgo: 1, win: false, enemyMid: OtherOpponent);

        await using var db = _fixture.CreateDbContext();
        var result = await CreateService(db).FindTopMatchesAsync(
            new CompositionSearchCriteria { ChampionId = Champion, Position = Position },
            CancellationToken.None);

        // No slot requested: every score is 0, recency orders the selection
        // and the confidence signals say so instead of faking certainty.
        result.MaxPossibleScore.Should().Be(0);
        result.MeanSimilarity.Should().Be(0);
        result.Matches.Select(m => m.MatchId).Should().Equal("COMP_NEWER", "COMP_OLDER");
        result.Matches.Should().OnlyContain(m => m.Score == 0);
    }

    [Fact]
    public async Task FindTopMatchesAsync_HonorsPositionQueueAndTopK()
    {
        await _fixture.ResetDatabaseAsync();

        // Noise the hard filters must drop: same champion in another lane, and
        // a wrong-queue game.
        await SeedGameAsync("COMP_WRONGLANE", daysAgo: 1, win: true, enemyMid: LaneOpponent, candidatePosition: "TOP");
        await SeedGameAsync("COMP_WRONGQUEUE", daysAgo: 1, win: true, enemyMid: LaneOpponent, queueId: 450);
        for (var i = 0; i < 3; i++)
        {
            await SeedGameAsync($"COMP_KEPT_{i}", daysAgo: 2 + i, win: true, enemyMid: LaneOpponent);
        }

        await using var db = _fixture.CreateDbContext();
        var result = await CreateService(db, topK: 2).FindTopMatchesAsync(
            new CompositionSearchCriteria
            {
                ChampionId = Champion,
                Position = Position,
                Enemies = new Dictionary<string, int> { ["MIDDLE"] = LaneOpponent },
            },
            CancellationToken.None);

        result.CandidatePoolSize.Should().Be(3, "the wrong-lane and wrong-queue games never enter the pool");
        result.Matches.Should().HaveCount(2, "TopK caps the selection");
        result.Matches.Select(m => m.MatchId).Should().Equal(
            "COMP_KEPT_0", "COMP_KEPT_1"); // equal scores → most recent first
    }

    private static CompositionMatchQueryService CreateService(Data.TrueMainDbContext db, int topK = 100)
        => new(
            db,
            Microsoft.Extensions.Options.Options.Create(
                new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(
                new CompositionSearchOptions { TopK = topK }));

    /// <summary>
    /// Seeds a full 10-participant game: the searched champion at
    /// <paramref name="candidatePosition"/> (participant 1, team 100), the
    /// requested-or-filler enemies on team 200, fillers elsewhere. Champion ids
    /// are offset high enough to never collide with the requested ones.
    /// </summary>
    private async Task SeedGameAsync(
        string matchId,
        int daysAgo,
        bool win,
        int enemyMid,
        int? enemyTop = null,
        int? allyJungle = null,
        string candidatePosition = Position,
        int queueId = 420)
    {
        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(queueId)
            .WithGameStartTimeUtc(DateTime.UtcNow.AddDays(-daysAgo))
            .Build());

        string[] positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];
        var participantId = 1;

        // Team 100 — the candidate first, fillers on the remaining lanes.
        db.MatchParticipants.Add(Participant(matchId, participantId++, Champion, 100, candidatePosition, win));
        foreach (var position in positions.Where(p => p != candidatePosition))
        {
            var championId = position == "JUNGLE" && allyJungle is { } jungle ? jungle : 900 + participantId;
            db.MatchParticipants.Add(Participant(matchId, participantId++, championId, 100, position, win));
        }

        // Team 200 — the requested enemies where provided, fillers elsewhere.
        foreach (var position in positions)
        {
            var championId = position switch
            {
                "MIDDLE" => enemyMid,
                "TOP" when enemyTop is { } top => top,
                _ => 900 + participantId,
            };
            db.MatchParticipants.Add(Participant(matchId, participantId++, championId, 200, position, !win));
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipant Participant(
        string matchId,
        int participantId,
        int championId,
        int teamId,
        string teamPosition,
        bool win)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            RiotAccountId = null,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = teamPosition,
            IndividualPosition = teamPosition,
            Lane = teamPosition,
            Role = "SOLO",
            Win = win,
            Kills = 5,
            Deaths = 4,
            Assists = 6,
            GoldEarned = 12000,
            TotalMinionsKilled = 180,
            NeutralMinionsKilled = 4,
            ChampLevel = 16,
            Item0 = 3153,
            Item1 = 3006,
            Item2 = 3031,
            Item3 = 0,
            Item4 = 0,
            Item5 = 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5001,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 4,
            Summoner2Id = 12,
            EloBracket = "",
            ItemEvents = [],
            SkillEvents = []
        };
}
