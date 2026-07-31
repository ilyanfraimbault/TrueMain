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
        bool withSnapshots = true)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("LaneOutcome")
            .WithTagLine("KR1")
            .WithPuuid("lane-outcome-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < games; i++)
        {
            var matchId = $"lane-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion(RawVersion)
                .WithTimelineIngested()
                .Build());

            db.MatchParticipants.Add(Participant(matchId, 1, Champion, teamId: 100, account.Id));
            db.MatchParticipants.Add(Participant(matchId, 2, Opponent, teamId: 200, riotAccountId: null));

            if (withSnapshots)
            {
                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 1, selfGold));
                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 2, opponentGold));
            }
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, Guid? riotAccountId)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
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

    private static MatchParticipantTimelineSnapshot Snapshot(string matchId, int participantId, int totalGold)
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
            Xp = 8000,
            Kills = 2,
            DamageToChampions = 5000,
        };
}
