using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionMatchupLeadAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const int Opponent = 238; // Zed
    private const string Position = "MIDDLE";
    private static readonly int[] Intervals = [5, 10, 15, 20, 30];

    // Opponent snapshots sit a fixed delta below the champion's, so each game
    // contributes exactly this lead at every interval.
    private const int GoldLead = 500;
    private const int CsLead = 5;
    private const int KillsLead = 1;
    private const int LevelLead = 1;
    private const int XpLead = 200;
    private const int DamageLead = 300;

    private readonly PostgresFixture _fixture;

    public ChampionMatchupLeadAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_AggregatesMatchupsAndTimelineLeadsFromRawRows()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var matchup = await db.ChampionMatchupStats.AsNoTracking().SingleAsync();
        matchup.ChampionId.Should().Be(Champion);
        matchup.TeamPosition.Should().Be(Position);
        matchup.OpponentChampionId.Should().Be(Opponent);
        matchup.Patch.Should().Be("16.4", "the raw GameVersion folds to major.minor");
        matchup.Games.Should().Be(12);
        matchup.Wins.Should().Be(7);

        // One row per canonical interval, each summing all 12 games' diffs.
        var leads = await db.ChampionTimelineLeadStats.AsNoTracking()
            .OrderBy(s => s.IntervalMinute)
            .ToListAsync();
        leads.Select(l => l.IntervalMinute).Should().Equal(Intervals);
        foreach (var lead in leads)
        {
            lead.ChampionId.Should().Be(Champion);
            lead.TeamPosition.Should().Be(Position);
            lead.Patch.Should().Be("16.4");
            lead.Games.Should().Be(12);
            lead.TotalGoldDiff.Should().Be(12L * GoldLead);
            lead.TotalCsDiff.Should().Be(12L * CsLead);
            lead.TotalKillsDiff.Should().Be(12L * KillsLead);
            lead.TotalLevelDiff.Should().Be(12L * LevelLead);
            lead.TotalXpDiff.Should().Be(12L * XpLead);
            lead.TotalDamageDiff.Should().Be(12L * DamageLead);
        }
    }

    [Fact]
    public async Task RunAsync_AggregatesAllChampionsInOneGlobalPass()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);
        // No main_champion_stats row for Ahri: she must reach the write path
        // purely through the global pass (tracked account on a non-main champion),
        // the case the per-champion work-list used to skip.
        await SeedGamesAsync(games: 8, version: "16.4.521.123", wins: 3, matchPrefix: "ahri", champion: 103, opponent: 134, main: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        // The single global pass must land each champion's rows separately and
        // untangled — counts stay per (champion, opponent), never merged.
        var matchups = await db.ChampionMatchupStats.AsNoTracking()
            .OrderBy(s => s.ChampionId)
            .ToListAsync();
        matchups.Should().HaveCount(2);

        var ahri = matchups.Single(m => m.ChampionId == 103);
        ahri.OpponentChampionId.Should().Be(134);
        ahri.Games.Should().Be(8);
        ahri.Wins.Should().Be(3);

        var yone = matchups.Single(m => m.ChampionId == Champion);
        yone.OpponentChampionId.Should().Be(Opponent);
        yone.Games.Should().Be(12);
        yone.Wins.Should().Be(7);

        var leadChampions = await db.ChampionTimelineLeadStats.AsNoTracking()
            .GroupBy(s => s.ChampionId)
            .Select(g => new { ChampionId = g.Key, Rows = g.Count() })
            .OrderBy(x => x.ChampionId)
            .ToListAsync();
        leadChampions.Select(x => x.ChampionId).Should().Equal(103, Champion);
        leadChampions.Should().OnlyContain(x => x.Rows == Intervals.Length);
    }

    [Fact]
    public async Task RunAsync_DoesNotDoubleCountOnRerunWithNoNewMatches()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionMatchupStats.CountAsync()).Should().Be(1, "the second run finds nothing pending (flag-gated) so no new rows are written");
        (await db.ChampionTimelineLeadStats.CountAsync()).Should().Be(Intervals.Length);

        var matchup = await db.ChampionMatchupStats.AsNoTracking().SingleAsync();
        matchup.Games.Should().Be(12, "counts must not double on a second run with nothing pending");

        (await db.Matches.CountAsync(m => !m.MatchupLeadAggregated)).Should().Be(0, "every seeded match was folded in on the first run");
    }

    [Fact]
    public async Task RunAsync_AccumulatesAcrossBatchesAsNewMatchesArrive()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        // More games for the same champion/opponent/patch arrive before the next cycle.
        await SeedGamesAsync(games: 5, version: "16.4.521.123", wins: 2, matchPrefix: "m2");
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var matchup = await db.ChampionMatchupStats.AsNoTracking().SingleAsync();
        matchup.Games.Should().Be(17, "the second batch's games must add to the first, not replace them");
        matchup.Wins.Should().Be(9);

        var leads = await db.ChampionTimelineLeadStats.AsNoTracking().ToListAsync();
        leads.Should().OnlyContain(l => l.Games == 17);
        leads.Single(l => l.IntervalMinute == 5).TotalGoldDiff.Should().Be(17L * GoldLead);
    }

    [Fact]
    public async Task RunAsync_KeepsAggregatesForPatchesWhoseMatchesWerePurged()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        // Simulate MatchDataRetention dropping the 16.4 match data, then a fresh
        // 16.5 patch arriving.
        await using (var mutate = _fixture.CreateDbContext())
        {
            mutate.MatchParticipantTimelineSnapshots.RemoveRange(await mutate.MatchParticipantTimelineSnapshots.ToListAsync());
            mutate.MatchParticipants.RemoveRange(await mutate.MatchParticipants.ToListAsync());
            mutate.Matches.RemoveRange(await mutate.Matches.ToListAsync());
            await mutate.SaveChangesAsync();
        }

        await SeedGamesAsync(games: 11, version: "16.5.1", wins: 4, matchPrefix: "m2");
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var matchups = await db.ChampionMatchupStats.AsNoTracking().ToListAsync();

        // The 16.4 row is frozen (its matches are gone so it can't be rebuilt) and
        // the live 16.5 patch produces its own row. Neither is wiped.
        matchups.Select(m => m.Patch).Should().BeEquivalentTo(["16.4", "16.5"]);
        matchups.Single(m => m.Patch == "16.4").Games.Should().Be(12);
        matchups.Single(m => m.Patch == "16.5").Games.Should().Be(11);
    }

    private ChampionMatchupLeadAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionMatchupLeadAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new MatchupLeadAggregationOptions()),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    private async Task SeedGamesAsync(
        int games, string version, int wins, string matchPrefix = "m", int champion = Champion, int opponent = Opponent,
        bool main = true)
    {
        await using var db = _fixture.CreateDbContext();

        // First seed creates the tracked account; later seeds (freeze test,
        // multi-champion test) reuse it (its Id is set client-side by the
        // builder, so it is usable before SaveChanges for the participant rows
        // below).
        var account = await db.RiotAccounts.FirstOrDefaultAsync();
        if (account is null)
        {
            account = new RiotAccountBuilder()
                .WithGameName("AggMain")
                .WithTagLine("KR1")
                .WithPuuid("agg-main-puuid")
                .Build();
            db.RiotAccounts.Add(account);
        }

        // The champion write-list unions main_champion_stats champions (#606
        // fix mirroring #604) with what the global pass surfaces; seeding with
        // main: false exercises the global-pass-only path.
        if (main && !await db.MainChampionStats.AnyAsync(s => s.Puuid == account.Puuid && s.ChampionId == champion))
        {
            db.MainChampionStats.Add(new MainChampionStat
            {
                PlatformId = account.PlatformId,
                Puuid = account.Puuid,
                ChampionId = champion,
                TotalMatches = games,
                ChampionMatches = games,
                PlayRate = 1.0,
                IsMain = true,
                PrimaryPosition = Position,
                CalculatedAtUtc = DateTime.UtcNow
            });
        }

        for (var i = 0; i < games; i++)
        {
            var matchId = $"{matchPrefix}-{version}-{i}";
            var match = new MatchBuilder().WithId(matchId).WithQueueId(QueueId).WithGameVersion(version).WithTimelineIngested().Build();
            db.Matches.Add(match);

            db.MatchParticipants.Add(Participant(matchId, 1, champion, teamId: 100, win: i < wins, riotAccountId: account.Id));
            db.MatchParticipants.Add(Participant(matchId, 2, opponent, teamId: 200, win: i >= wins));

            foreach (var minute in Intervals)
            {
                var gold = minute * 200;
                var cs = minute * 5;
                var level = minute / 3 + 4;
                var xp = minute * 250;
                var kills = minute / 5;
                var damage = minute * 200;

                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 1, minute, gold, cs, level, xp, kills, damage));
                db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, 2, minute,
                    gold - GoldLead, cs - CsLead, level - LevelLead, xp - XpLead, kills - KillsLead, damage - DamageLead));
            }
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipantTimelineSnapshot Snapshot(
        string matchId, int participantId, int minute,
        int gold, int cs, int level, int xp, int kills, int damage)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = minute,
            TimestampMs = minute * 60_000,
            TotalGold = gold,
            MinionsKilled = cs,
            JungleMinionsKilled = 0,
            Level = level,
            Xp = xp,
            Kills = kills,
            DamageToChampions = damage,
            WardsPlaced = 0,
            WardsKilled = 0
        };

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, bool win, Guid? riotAccountId = null)
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
            Win = win,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = []
        };
}
