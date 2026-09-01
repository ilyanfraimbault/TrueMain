using AwesomeAssertions;
using Core.Lol.Map;
using Core.Lol.Ranking;
using Core.Options;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the lane-outcome fold (#919) against real Postgres: the upsert that adds
/// lane counters onto an existing <c>champion_matchup_stats</c> row, and the three
/// outcomes a gold *threshold* produces — won, lost, and the band in between that must
/// end up in neither. Also the signed gap those outcomes came from (#976), which is
/// summed over every judged lane rather than the decided ones.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionLaneOutcomeAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157;    // The tracked side.
    private const int Opponent = 238;    // Their lane opponent.
    private const string Position = "MIDDLE";
    private const string RawVersion = "16.4.521.123";
    private const int Threshold = 300;

    /// <summary>Experience both sides carry unless a case sets them apart.</summary>
    private const int DefaultXp = 8_000;

    private readonly PostgresFixture _fixture;

    public ChampionLaneOutcomeAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_CountsALaneWonPastTheThreshold()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 4, selfGold: 6000, opponentGold: 5000);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.LaneGames.Should().Be(4);
        stat.LaneWins.Should().Be(4, "1000 gold clears the 300 threshold");
        stat.LaneLosses.Should().Be(0);
        stat.LaneGoldDiffSum.Should().Be(4000, "the gap itself is summed, not just its verdict (#976)");
        stat.LaneGoldDiffGames.Should().Be(4);
    }

    [Fact]
    public async Task RunAsync_CountsALaneLostPastTheThreshold()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 3, selfGold: 5000, opponentGold: 6000);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.LaneGames.Should().Be(3);
        stat.LaneWins.Should().Be(0);
        stat.LaneLosses.Should().Be(3);
        stat.LaneGoldDiffSum.Should().Be(-3000, "the sum is signed — a lost lane pulls it negative");
        stat.LaneGoldDiffGames.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Threshold)]
    [InlineData(-Threshold)]
    public async Task RunAsync_LeavesALaneInsideTheBandUndecided(int lead)
    {
        await _fixture.ResetDatabaseAsync();
        // The whole reason wins and losses are stored separately: a threshold creates a
        // third outcome. Exactly ±threshold is inside the band — the comparison is
        // strict — so these lanes count as judged but decide nothing, and must not be
        // inferable as losses from LaneGames - LaneWins.
        await SeedGamesAsync(games: 5, selfGold: 5000 + lead, opponentGold: 5000);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.LaneGames.Should().Be(5, "the lane was judgeable — both sides had a 15-min reading");
        stat.LaneWins.Should().Be(0);
        stat.LaneLosses.Should().Be(0);
        (stat.LaneGames - stat.LaneWins - stat.LaneLosses)
            .Should().Be(5, "evens are recoverable as games minus wins minus losses");

        // The gap is measured on every *judged* lane, evens included — it is what the
        // counters cannot express. Averaging it over decided lanes only would drop the
        // very games that make a matchup even (#976).
        stat.LaneGoldDiffGames.Should().Be(5, "an even lane still has a measured gap");
        stat.LaneGoldDiffSum.Should().Be(5L * lead);
    }

    [Fact]
    public async Task RunAsync_SumsTheExperienceGapOnItsOwnCounter()
    {
        await _fixture.ResetDatabaseAsync();
        // Ahead in gold, behind in experience (#1111): a lane won on kills and lost on
        // waves. This is the case both counters exist for — deriving XP from gold, or
        // banding it with the gold verdict, would report a very good lane where the
        // champion is a level down and the next all-in flips it.
        await SeedGamesAsync(
            games: 4, selfGold: 6_000, opponentGold: 5_000,
            selfXp: 7_400, opponentXp: 8_000);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.LaneGoldDiffSum.Should().Be(4_000, "the gold gap is unchanged by the XP one");
        stat.LaneXpDiffSum.Should().Be(-2_400, "signed the same way, and pointing the other direction here");
        stat.LaneXpDiffGames.Should().Be(4);
        stat.LaneXpDiffGames.Should().Be(stat.LaneGoldDiffGames,
            "both gaps come off the same 15-minute snapshot, so they cover the same lanes going forward");
    }

    [Fact]
    public async Task RunAsync_IgnoresAMatchWithNoFifteenMinuteSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        // A game that ended early, or whose timeline was never ingested: a real game
        // but not a judgeable lane. Counting it in LaneGames would understate the lane
        // win rate; that is exactly why LaneGames is separate from Games.
        await SeedGamesAsync(games: 6, selfGold: 9000, opponentGold: 1000, withSnapshots: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var stats = await db.ChampionMatchupStats.AsNoTracking().ToListAsync();
        stats.Should().BeEmpty("no judgeable lane means nothing for this fold to write");

        (await db.Matches.CountAsync(m => !m.LaneOutcomeAggregated))
            .Should().Be(0, "an unproductive match is still flagged, or it would be re-read forever");
    }

    [Fact]
    public async Task RunAsync_LeavesAMatchWhoseTimelineHasNotArrivedForALaterRun()
    {
        await _fixture.ResetDatabaseAsync();
        // Timeline still pending — the ordinary case, not a corruption:
        // TimelineIngestionService leaves TimelineIngested false on a truncated payload
        // and re-fetches the match on a later run, so its 15-minute snapshots are simply
        // not there yet. Folding it now would flag it as done for a zero contribution and
        // nothing would ever look at it again (#1223).
        await SeedGamesAsync(
            games: 3, selfGold: 6000, opponentGold: 5000,
            withSnapshots: false, timelineIngested: false);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await using (var afterFirstRun = _fixture.CreateDbContext())
        {
            (await afterFirstRun.ChampionMatchupStats.AsNoTracking().CountAsync())
                .Should().Be(0, "there is nothing to judge without the 15-minute readings");
            (await afterFirstRun.Matches.CountAsync(m => m.LaneOutcomeAggregated))
                .Should().Be(0, "a match still waiting for its timeline must stay pending");
        }

        // The timeline lands, exactly as a later MatchIngestion run writes it.
        await using (var timelineArrival = _fixture.CreateDbContext())
        {
            foreach (var match in await timelineArrival.Matches.ToListAsync())
            {
                match.TimelineIngested = true;
                timelineArrival.MatchParticipantTimelineSnapshots.Add(Snapshot(match.Id, 1, totalGold: 6000));
                timelineArrival.MatchParticipantTimelineSnapshots.Add(Snapshot(match.Id, 2, totalGold: 5000));
            }

            await timelineArrival.SaveChangesAsync();
        }

        await process.RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.LaneGames.Should().Be(3, "the fold picks the match up on the run after its timeline arrives");
        stat.LaneWins.Should().Be(3);
        stat.LaneGoldDiffSum.Should().Be(3000, "the gap is folded too, not just the verdict");
    }

    [Fact]
    public async Task RunAsync_AddsOntoAnExistingMatchupRowRatherThanDuplicatingIt()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 4, selfGold: 6000, opponentGold: 5000);

        // The row the sibling matchup fold would already have written for this grain.
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.ChampionMatchupStats.Add(new ChampionMatchupStat
            {
                Id = Guid.NewGuid(),
                ChampionId = Champion,
                TeamPosition = Position,
                OpponentChampionId = Opponent,
                Patch = "16.4",
                EloBracket = EloBracket.Gold,
                Games = 4,
                Wins = 3,
                AggregatedAtUtc = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.Games.Should().Be(4, "this fold must not touch the game counters");
        stat.Wins.Should().Be(3);
        stat.LaneGames.Should().Be(4);
        stat.LaneWins.Should().Be(4);
    }

    [Fact]
    public async Task RunAsync_DoesNotDoubleCountOnRerun()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 4, selfGold: 6000, opponentGold: 5000);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.LaneGames.Should().Be(4, "the second run has nothing pending");
        stat.LaneWins.Should().Be(4);
        stat.LaneGoldDiffSum.Should().Be(4000, "an additive sum is exactly what a re-run would double");
        stat.LaneGoldDiffGames.Should().Be(4);
    }

    private async Task<ChampionMatchupStat> SingleStatAsync()
    {
        await using var db = _fixture.CreateDbContext();
        return await db.ChampionMatchupStats.AsNoTracking().SingleAsync();
    }

    private ChampionLaneOutcomeAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionLaneOutcomeAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new LaneOutcomeAggregationOptions { GoldLeadThreshold = Threshold }),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    /// <summary>
    /// Seeds head-to-head games: the tracked player on <see cref="Champion"/> against
    /// <see cref="Opponent"/> in the same lane, each with a 15-minute snapshot carrying
    /// the requested gold on both sides.
    /// </summary>
    private async Task SeedGamesAsync(
        int games,
        int selfGold,
        int opponentGold,
        bool withSnapshots = true,
        int selfXp = DefaultXp,
        int opponentXp = DefaultXp,
        bool timelineIngested = true)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("LaneOutcome")
            .WithTagLine("KR1")
            .WithPuuid("lane-outcome-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        // The fold's champion-side cohort is "main of this champion"
        // (Data.Aggregation.MatchupCohort), joined on (platform, puuid, champion) —
        // so a tracked account alone no longer folds anything, and the participant
        // rows below must carry the account's real puuid the way RiotMatchMapper
        // writes them.
        db.MainChampionStats.Add(new MainChampionStat
        {
            PlatformId = account.PlatformId,
            Puuid = account.Puuid,
            ChampionId = Champion,
            TotalMatches = games,
            ChampionMatches = games,
            PlayRate = 1.0,
            IsMain = true,
            PrimaryPosition = Position,
            CalculatedAtUtc = DateTime.UtcNow,
        });

        for (var i = 0; i < games; i++)
        {
            var matchId = $"lane-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion(RawVersion)
                .WithTimelineIngested(timelineIngested)
                .Build());

            db.MatchParticipants.Add(Participant(matchId, 1, Champion, teamId: 100, account.Id, account.Puuid));
            db.MatchParticipants.Add(Participant(matchId, 2, Opponent, teamId: 200, riotAccountId: null));

            if (withSnapshots)
            {
                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 1, selfGold, selfXp));
                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 2, opponentGold, opponentXp));
            }
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, Guid? riotAccountId, string? puuid = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = puuid ?? $"puuid-{matchId}-{participantId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            Win = true,
            ChampLevel = 16,
            EloBracket = EloBracket.Gold,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = [],
        };

    private static MatchParticipantTimelineSnapshot Snapshot(
        string matchId, int participantId, int totalGold, int xp = DefaultXp)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = 15,
            TimestampMs = 15 * 60 * 1000,
            TotalGold = totalGold,
            MinionsKilled = 100,
            JungleMinionsKilled = 0,
            Level = 11,
            Xp = xp,
            Kills = 2,
            DamageToChampions = 5000,
        };
}
