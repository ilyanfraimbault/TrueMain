using AwesomeAssertions;
using Core.Lol.Map;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the lane half of the matchup fold (#919, merged into it by #1445) against
/// real Postgres: the three outcomes a gold *threshold* produces — won, lost, and the
/// band in between that must end up in neither — the signed gap those outcomes came
/// from (#976), summed over every judged lane rather than the decided ones, and the
/// invariant the merge exists for: a match's game counters and its lane counters always
/// land on the same row, whatever happens to its elo bracket between runs.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionMatchupLaneOutcomeIntegrationTests
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

    public ChampionMatchupLaneOutcomeIntegrationTests(PostgresFixture fixture)
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
    public async Task RunAsync_CountsAMatchWithNoFifteenMinuteSnapshotAsAGameButNotALane()
    {
        await _fixture.ResetDatabaseAsync();
        // A game that ended before the mark: a real game but not a judgeable lane.
        // Counting it in LaneGames would understate the lane win rate; that is exactly
        // why LaneGames is separate from Games — and why Games >= LaneGames is the
        // normal reading of a row.
        await SeedGamesAsync(games: 6, selfGold: 9000, opponentGold: 1000, withSnapshots: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.Games.Should().Be(6, "the game side does not need a timeline");
        stat.LaneGames.Should().Be(0, "no reading, no verdict");
        stat.LaneGoldDiffGames.Should().Be(0);

        await using var db = _fixture.CreateDbContext();
        (await db.Matches.CountAsync(m => !m.MatchupLeadAggregated))
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
            (await afterFirstRun.Matches.CountAsync(m => m.MatchupLeadAggregated))
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
    public async Task RunAsync_AddsOntoTheRowAnEarlierFoldWroteRatherThanDuplicatingIt()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 4, selfGold: 6000, opponentGold: 5000);

        // The row an earlier pass left for this grain — a patch is folded over many
        // runs, so every counter has to land additively on the row already there.
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
                LaneGames = 4,
                LaneWins = 2,
                AggregatedAtUtc = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        var stat = await SingleStatAsync();
        stat.Games.Should().Be(8, "the four seeded games plus the four just folded");
        stat.Wins.Should().Be(7);
        stat.LaneGames.Should().Be(8);
        stat.LaneWins.Should().Be(6);
    }

    [Fact]
    public async Task RunAsync_KeepsAMatchsGameAndLaneCountersOnOneRowWhenItsBracketIsStampedLater()
    {
        await _fixture.ResetDatabaseAsync();
        // The defect #1445 closes. elo_bracket is part of the row key and is stamped
        // asynchronously (MatchParticipantEloBracketEnrichment), so while the two folds
        // were separate a match could be counted as a game under one bracket and as a
        // lane under another — leaving a row with more LaneGames than Games, which no
        // count of anything can be. Here the bracket changes *between* two runs, the
        // hardest case for one fold and the ordinary one for two.
        await SeedGamesAsync(games: 2, selfGold: 6000, opponentGold: 5000, eloBracket: "");

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        // Enrichment stamps the band, and two more games of the same matchup arrive.
        await using (var stamping = _fixture.CreateDbContext())
        {
            await stamping.MatchParticipants
                .Where(p => p.RiotAccountId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EloBracket, EloBracket.Master));
        }

        await SeedGamesAsync(
            games: 2, selfGold: 6000, opponentGold: 5000,
            eloBracket: EloBracket.Master, idPrefix: "stamped", seedAccount: false);

        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var stats = await db.ChampionMatchupStats.AsNoTracking().ToListAsync();

        stats.Should().HaveCount(2, "the bracket is part of the key, so the two bands are two rows");
        stats.Should().OnlyContain(
            s => s.LaneGames <= s.Games,
            "a lane is judged out of a game that was counted on the same row, never out of another row's");
        stats.Should().OnlyContain(s => s.Games == 2 && s.LaneGames == 2);
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

    private ChampionMatchupLeadAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionMatchupLeadAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new MatchupLeadAggregationOptions()),
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
        bool timelineIngested = true,
        string eloBracket = EloBracket.Gold,
        string idPrefix = "lane",
        bool seedAccount = true)
    {
        await using var db = _fixture.CreateDbContext();

        var account = seedAccount
            ? SeedAccount(db, games)
            : await db.RiotAccounts.SingleAsync();

        for (var i = 0; i < games; i++)
        {
            var matchId = $"{idPrefix}-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion(RawVersion)
                .WithTimelineIngested(timelineIngested)
                .Build());

            db.MatchParticipants.Add(
                Participant(matchId, 1, Champion, teamId: 100, account.Id, eloBracket, account.Puuid));
            db.MatchParticipants.Add(
                Participant(matchId, 2, Opponent, teamId: 200, riotAccountId: null, eloBracket));

            if (withSnapshots)
            {
                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 1, selfGold, selfXp));
                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 2, opponentGold, opponentXp));
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The tracked account and the main row that puts it in the cohort.</summary>
    private static RiotAccount SeedAccount(TrueMainDbContext db, int games)
    {
        var account = new RiotAccountBuilder()
            .WithGameName("LaneOutcome")
            .WithTagLine("KR1")
            .WithPuuid("lane-outcome-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        // The fold's champion-side cohort is "main of this champion"
        // (Data.Aggregation.ChampionCohort), joined on (platform, puuid, champion) —
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

        return account;
    }

    private static MatchParticipant Participant(
        string matchId,
        int participantId,
        int championId,
        int teamId,
        Guid? riotAccountId,
        string eloBracket,
        string? puuid = null)
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
            EloBracket = eloBracket,
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
