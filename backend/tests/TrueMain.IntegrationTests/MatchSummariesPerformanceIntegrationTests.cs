using AwesomeAssertions;
using Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the scoring half of the match-history feed (#918): the timeline-mark
/// and kill-position bulk loads, and the performance score / placement /
/// MVP / ACE the collapsed row is badged from.
///
/// <para>The headline case is <see cref="GetAsync_agrees_with_the_match_detail_service_on_the_same_game"/>
/// plus <see cref="GetAsync_denies_the_accolade_to_the_best_raw_kda_on_the_winning_side"/>: the feed used to
/// derive MVP/ACE from a raw KDA proxy while the detail payload behind the very
/// same row used the real scorer, so a row could badge a player MVP and the
/// expanded panel disagree. Those two tests pin that both services now read the
/// same ranking, after a real Postgres round trip.</para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchSummariesPerformanceIntegrationTests
{
    private const string NameTag = "PerfFeed-KR1";
    private const string Puuid = "puuid-match-summaries-performance";
    private const string MatchId = "PERF_FEED_MATCH_1";

    /// <summary>The canonical marks the ingestor stores, and the ones the scorer folds.</summary>
    private static readonly int[] CanonicalMinutes = [5, 10, 15, 20, 30];

    private readonly PostgresFixture _fixture;

    public MatchSummariesPerformanceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAsync_scores_and_places_the_row_from_a_full_match()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedMatchAsync();

        await using var db = _fixture.CreateDbContext();
        var self = await SelfRowAsync(db);

        // The queries the scoring path added (timeline marks, kill positions and
        // the GroupBy on (ParticipantId, Minute)) have to survive translation
        // before any of this is even reachable.
        self.PerformanceScore.Should().BeInRange(1, 100,
            "a fully-populated match scores every component, so the row can't come back at the 0 floor");
        self.Placement.Should().BeInRange(1, 10);
    }

    [Fact]
    public async Task GetAsync_agrees_with_the_match_detail_service_on_the_same_game()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedMatchAsync();

        await using var db = _fixture.CreateDbContext();
        var self = await SelfRowAsync(db);

        var detail = await new MatchDetailQueryService(db)
            .GetAsync(NameTag, MatchId, CancellationToken.None);
        detail.Should().NotBeNull();
        var detailSelf = detail!.Participants.Single(p => p.ParticipantId == SelfParticipantId);

        // The invariant the shared PerformanceInputs entry point exists to
        // guarantee: the collapsed row and the panel behind it are the same game.
        self.PerformanceScore.Should().Be(detailSelf.PerformanceScore);
        self.Placement.Should().Be(detailSelf.Placement);
        self.IsMvp.Should().Be(detailSelf.IsMvp);
        self.IsAce.Should().Be(detailSelf.IsAce);
    }

    [Fact]
    public async Task GetAsync_denies_the_accolade_to_the_best_raw_kda_on_the_winning_side()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedMatchAsync();

        await using var db = _fixture.CreateDbContext();
        var self = await SelfRowAsync(db);

        // The seeded player has the winning side's best raw KDA — 2/0/8 reads as
        // 10.0 against the jungler's 22/4 = 5.5 — but no damage, no farm, no
        // vision and a lost lane. The retired proxy crowned exactly this; the
        // real scorer must not.
        self.Win.Should().BeTrue();
        self.IsMvp.Should().BeFalse();
        self.IsAce.Should().BeFalse("the accolade for a lost game never lands on a winner");

        var detail = await new MatchDetailQueryService(db)
            .GetAsync(NameTag, MatchId, CancellationToken.None);
        var mvp = detail!.Participants.Single(p => p.IsMvp);
        mvp.ParticipantId.Should().Be(CarryParticipantId, "the carry outscores the padded KDA line");
        mvp.Win.Should().BeTrue();

        detail.Participants.Single(p => p.IsAce).Win.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_still_scores_a_match_with_no_timeline_at_all()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedMatchAsync(withTimeline: false);

        await using var db = _fixture.CreateDbContext();
        var self = await SelfRowAsync(db);

        // No marks and no kill positions: the lead and roam components drop and
        // their weight is redistributed, rather than the row failing or the
        // player being scored a zero for a gap in our data.
        self.PerformanceScore.Should().BeInRange(1, 100);
        self.Placement.Should().BeInRange(1, 10);
    }

    // ── Fixture ─────────────────────────────────────────────────────────────

    private const int SelfParticipantId = 1;
    private const int CarryParticipantId = 2;

    private async Task<MatchSummarySelfReadModel> SelfRowAsync(Data.TrueMainDbContext db)
    {
        var response = await new MatchSummariesQueryService(
                db,
                new MatchSummaryHydrator(db, NullLogger<MatchSummaryHydrator>.Instance),
                NullLogger<MatchSummariesQueryService>.Instance)
            .GetAsync(NameTag, page: 1, pageSize: 20, position: null, championId: null, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Matches.Should().ContainSingle();
        return response.Matches[0].Self;
    }

    private async Task SeedAccountAsync()
    {
        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccountBuilder()
            .WithGameName("PerfFeed")
            .WithTagLine("KR1")
            .WithPuuid(Puuid)
            .Build());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// One complete ranked game: ten participants (one per lane per side),
    /// timeline snapshots at every canonical mark, and early kill positions.
    ///
    /// <para>Participant 1 is the tracked player — MIDDLE, blue side, winner,
    /// with the winning side's best raw KDA and nothing else. Participant 2 is
    /// the jungle carry who should take the MVP. Everyone else is filler.</para>
    /// </summary>
    /// <param name="withTimeline">
    /// When false, no snapshots and no kill positions are written, so the lead
    /// and roam components drop.
    /// </param>
    private async Task SeedMatchAsync(bool withTimeline = true)
    {
        var positions = new[] { "MIDDLE", "JUNGLE", "TOP", "BOTTOM", "UTILITY" };

        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new MatchBuilder()
            .WithId(MatchId)
            .WithGameDurationSeconds(1_800)
            .WithTimelineIngested(withTimeline)
            .Build());

        for (var participantId = 1; participantId <= 10; participantId++)
        {
            var teamId = participantId <= 5 ? 100 : 200;
            var position = positions[(participantId - 1) % 5];
            var isSelf = participantId == SelfParticipantId;
            var isCarry = participantId == CarryParticipantId;

            db.MatchParticipants.Add(new MatchParticipant
            {
                MatchId = MatchId,
                ParticipantId = participantId,
                Puuid = isSelf ? Puuid : $"puuid-{MatchId}-{participantId}",
                RiotAccountId = null,
                SummonerName = isSelf ? "PerfFeed" : $"Player{participantId}",
                SummonerLevel = 100,
                ChampionId = 100 + participantId,
                TeamId = teamId,
                TeamPosition = position,
                IndividualPosition = position,
                Lane = position,
                Role = "SOLO",
                Win = teamId == 100,
                // Self: a padded 2/0/8 — the best raw (k+a)/max(1,deaths) on the
                // winning side at 10.0, against the carry's 5.5.
                Kills = isSelf ? 2 : isCarry ? 12 : 1,
                Deaths = isSelf ? 0 : isCarry ? 4 : 5,
                Assists = isSelf ? 8 : isCarry ? 10 : 3,
                TotalDamageDealtToChampions = isSelf ? 3_000 : isCarry ? 35_000 : 8_000,
                VisionScore = isSelf ? 5 : isCarry ? 30 : 15,
                GoldEarned = isSelf ? 6_000 : isCarry ? 18_000 : 9_000,
                TotalMinionsKilled = isSelf ? 25 : isCarry ? 200 : 110,
                NeutralMinionsKilled = isSelf ? 5 : isCarry ? 40 : 10,
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

            if (!withTimeline)
            {
                continue;
            }

            foreach (var minute in CanonicalMinutes)
            {
                // Gold / cs / xp scale with the minute so the leads stay
                // plausible at every mark. Self is behind their lane opponent,
                // the carry ahead of theirs, everyone else level.
                var factor = isSelf ? 0.7d : isCarry ? 1.3d : 1.0d;
                db.MatchParticipantTimelineSnapshots.Add(new MatchParticipantTimelineSnapshot
                {
                    MatchId = MatchId,
                    ParticipantId = participantId,
                    IntervalMinute = minute,
                    TimestampMs = minute * 60_000,
                    TotalGold = (int)(minute * 400 * factor),
                    MinionsKilled = (int)(minute * 7 * factor),
                    JungleMinionsKilled = 5,
                    Level = Math.Min(18, 1 + (minute / 2)),
                    Xp = (int)(minute * 450 * factor),
                    Kills = 1,
                    DamageToChampions = minute * 400,
                    WardsPlaced = 2,
                    WardsKilled = 1,
                });
            }

            // Two early kill participations each: one at home, one away. Mid
            // lane sits on the map's main diagonal, bot lane on the flat
            // stretch red side of the river — so the second one is a roam for
            // everyone whose own lane is not BOT.
            db.MatchParticipantKillPositions.Add(new MatchParticipantKillPosition
            {
                MatchId = MatchId,
                ParticipantId = participantId,
                TimestampMs = 300_000,
                X = 7_400,
                Y = 7_400,
            });
            db.MatchParticipantKillPositions.Add(new MatchParticipantKillPosition
            {
                MatchId = MatchId,
                ParticipantId = participantId,
                TimestampMs = 600_000,
                X = 11_000,
                Y = 1_100,
            });
        }

        await db.SaveChangesAsync();
    }
}
