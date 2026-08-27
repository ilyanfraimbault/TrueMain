using AwesomeAssertions;
using Core.Options;
using Data.Entities;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.Services.Truemains;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the read path of the player-scoped performance panel (#918)
/// against real Postgres: the queries translate, the sample floor is honoured,
/// and the per-component sample counts reflect which signals each game
/// actually carried.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PlayerChampionPerformanceQueryServiceIntegrationTests : IDisposable
{
    private const string NameTag = "PerfSummoner-KR1";
    private const string Puuid = "puuid-player-champion-performance";
    private const int ChampionId = 157;

    private readonly PostgresFixture _fixture;

    // One cache for the class, disposed with it. Entries are keyed by the
    // account's id, which a fresh seed regenerates on every reset, so no test
    // can be served another's response.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });

    public PlayerChampionPerformanceQueryServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_account()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        var response = await CreateService(db).GetAsync(
            "NoSuchPlayer-KR1", ChampionId, patch: null, position: null, CancellationToken.None);

        response.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_suppresses_the_averages_below_the_sample_floor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedGamesAsync(count: PlayerChampionPerformanceQueryService.MinGames - 1);

        await using var db = _fixture.CreateDbContext();
        var response = await CreateService(db).GetAsync(
            NameTag, ChampionId, patch: null, position: null, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Games.Should().Be(PlayerChampionPerformanceQueryService.MinGames - 1);
        response.MinGames.Should().Be(PlayerChampionPerformanceQueryService.MinGames);
        response.AverageScore.Should().BeNull("a thin sample is reported as counts, never as a confident average");
        response.Components.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_grades_the_sample_and_reports_per_component_coverage()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedGamesAsync(count: 6, timelineOnFirstGamesOnly: 4);

        await using var db = _fixture.CreateDbContext();
        var response = await CreateService(db).GetAsync(
            NameTag, ChampionId, patch: null, position: null, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Games.Should().Be(6);
        response.Window.Should().Be(PlayerChampionPerformanceQueryService.Window);
        response.AverageScore.Should().BeInRange(0d, 100d);
        response.BestScore.Should().BeGreaterThanOrEqualTo(response.WorstScore!.Value);
        response.TopOfTeamRate.Should().BeInRange(0d, 1d);

        // One entry per component of the model, always, so a caller can index
        // them without worrying about which ones this sample happened to have.
        response.Components.Should().HaveCount(9);

        // Combat needs no timeline, so it covers every game; laning only exists
        // for the four games we gave a @15 snapshot to.
        var combat = response.Components.Single(c => c.Kind == "Combat");
        combat.Games.Should().Be(6);
        combat.Value.Should().NotBeNull();

        var laning = response.Components.Single(c => c.Kind == "Laning");
        laning.Games.Should().Be(4, "a game with no timeline lowers the component's sample, not its average");
        laning.Value.Should().NotBeNull();

        // No kill positions were seeded at all, so roam is unknown everywhere —
        // reported as zero games and a null value rather than a zero grade.
        var roam = response.Components.Single(c => c.Kind == "Roam");
        roam.Games.Should().Be(0);
        roam.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_scopes_the_sample_to_the_requested_position()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedGamesAsync(count: 6);

        await using var db = _fixture.CreateDbContext();
        var response = await CreateService(db).GetAsync(
            NameTag, ChampionId, patch: null, position: "TOP", CancellationToken.None);

        response.Should().NotBeNull();
        response!.Position.Should().Be("TOP");
        response.Games.Should().Be(0, "every seeded game was played MIDDLE");
        response.AverageScore.Should().BeNull();
    }

    private PlayerChampionPerformanceQueryService CreateService(Data.TrueMainDbContext db)
        => new(
            db,
            new TruemainAccountResolver(db),
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions()),
            _cache);

    private async Task SeedAccountAsync()
    {
        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccountBuilder()
            .WithGameName("PerfSummoner")
            .WithTagLine("KR1")
            .WithPuuid(Puuid)
            .Build());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds <paramref name="count"/> ranked games, each with a full 10-player
    /// roster so the share components have real team totals. The player is
    /// participant 1 (MIDDLE, blue side) and their lane opponent is participant
    /// 8, the other MIDDLE.
    /// </summary>
    /// <param name="count">Number of games to seed.</param>
    /// <param name="timelineOnFirstGamesOnly">
    /// How many of the games get @15 timeline snapshots. Null means all of them.
    /// </param>
    private async Task SeedGamesAsync(int count, int? timelineOnFirstGamesOnly = null)
    {
        var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        var withTimeline = timelineOnFirstGamesOnly ?? count;

        await using var db = _fixture.CreateDbContext();

        for (var game = 0; game < count; game++)
        {
            var matchId = $"PERF_MATCH_{game}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithGameStartTimeUtc(DateTime.UtcNow.AddHours(-game - 1))
                .WithTimelineIngested()
                .Build());

            for (var participantId = 1; participantId <= 10; participantId++)
            {
                var isSelf = participantId == 1;
                var teamId = participantId <= 5 ? 100 : 200;
                // Participant 1 is TOP by index, so shift the player into MIDDLE
                // explicitly and let the rest fill the remaining slots — the
                // point of the fixture is a well-formed 1-per-lane-per-side
                // roster, not a realistic draft.
                var position = isSelf ? "MIDDLE" : positions[(participantId - 1) % 5];
                if (participantId == 3)
                {
                    position = "TOP";
                }

                db.MatchParticipants.Add(new MatchParticipant
                {
                    MatchId = matchId,
                    ParticipantId = participantId,
                    Puuid = isSelf ? Puuid : $"puuid-{matchId}-{participantId}",
                    RiotAccountId = null,
                    SummonerName = isSelf ? "PerfSummoner" : $"Player{participantId}",
                    SummonerLevel = 100,
                    ChampionId = isSelf ? ChampionId : 100 + participantId,
                    TeamId = teamId,
                    TeamPosition = position,
                    IndividualPosition = position,
                    Lane = position,
                    Role = "SOLO",
                    Win = teamId == 100,
                    Kills = isSelf ? 8 : 3,
                    Deaths = isSelf ? 3 : 5,
                    Assists = isSelf ? 7 : 4,
                    TotalDamageDealtToChampions = isSelf ? 25_000 : 14_000,
                    VisionScore = isSelf ? 24 : 18,
                    GoldEarned = isSelf ? 14_000 : 11_000,
                    TotalMinionsKilled = isSelf ? 210 : 160,
                    NeutralMinionsKilled = 10,
                    ChampLevel = 16,
                    Item0 = 3153,
                    Item1 = 3006,
                    Item2 = 0,
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
                    EloBracket = string.Empty,
                    ItemEvents = [],
                    SkillEvents = [],
                });

                if (game >= withTimeline)
                {
                    continue;
                }

                db.MatchParticipantTimelineSnapshots.Add(new MatchParticipantTimelineSnapshot
                {
                    MatchId = matchId,
                    ParticipantId = participantId,
                    IntervalMinute = 15,
                    TimestampMs = 900_000,
                    TotalGold = isSelf ? 6_200 : 5_600,
                    MinionsKilled = isSelf ? 135 : 115,
                    JungleMinionsKilled = 5,
                    Level = 9,
                    Xp = isSelf ? 7_200 : 6_600,
                    Kills = 1,
                    DamageToChampions = 8_000,
                    WardsPlaced = 3,
                    WardsKilled = 1,
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
