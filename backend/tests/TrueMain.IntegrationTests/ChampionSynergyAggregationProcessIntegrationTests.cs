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

/// <summary>
/// Exercises the synergy fold (#922) against real Postgres, which is where the
/// parts a unit test cannot reach live: the two <c>ON CONFLICT</c> upserts, and
/// the per-match flag that has to make a re-run a no-op.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionSynergyAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone, MIDDLE, the tracked side.
    private const string Position = "MIDDLE";

    // The rest of the tracked player's team, one per remaining canonical lane.
    private static readonly (int ChampionId, string Position)[] Allies =
    [
        (86, "TOP"),      // Garen
        (64, "JUNGLE"),   // Lee Sin
        (81, "BOTTOM"),   // Ezreal
        (350, "UTILITY")  // Yuumi
    ];

    // The enemy team. Same lanes, different champions — none of these may ever
    // appear as a partner.
    private static readonly (int ChampionId, string Position)[] Enemies =
    [
        (122, "TOP"),
        (60, "JUNGLE"),
        (238, "MIDDLE"),
        (222, "BOTTOM"),
        (412, "UTILITY")
    ];

    private readonly PostgresFixture _fixture;

    public ChampionSynergyAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_FoldsOneRowPerTeammate_AndNoneForOpponents()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var pairs = await db.ChampionSynergyStats.AsNoTracking().ToListAsync();
        pairs.Should().HaveCount(Allies.Length, "one row per teammate lane, never one per opponent");
        pairs.Should().AllSatisfy(pair =>
        {
            pair.ChampionId.Should().Be(Champion);
            pair.TeamPosition.Should().Be(Position);
            pair.Patch.Should().Be("16.4", "the raw GameVersion folds to major.minor");
            pair.Games.Should().Be(12);
            pair.Wins.Should().Be(7);
        });

        pairs.Select(pair => (pair.PartnerChampionId, pair.PartnerPosition))
            .Should().BeEquivalentTo(Allies);

        pairs.Select(pair => pair.PartnerChampionId)
            .Should().NotIntersectWith(Enemies.Select(enemy => enemy.ChampionId));
    }

    [Fact]
    public async Task RunAsync_WritesSelfAndAllyBaselinesForTheSameGames()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var baselines = await db.ChampionSynergyBaselineStats.AsNoTracking().ToListAsync();

        // Exactly one SELF row — the tracked player — so summing the SELF side
        // gives the cohort's game count without counting a match five times.
        var self = baselines.Where(b => b.Side == SynergyBaselineSide.Self).ToList();
        self.Should().ContainSingle();
        self[0].ChampionId.Should().Be(Champion);
        self[0].TeamPosition.Should().Be(Position);
        self[0].Games.Should().Be(12);
        self[0].Wins.Should().Be(7);

        var ally = baselines.Where(b => b.Side == SynergyBaselineSide.Ally).ToList();
        ally.Select(b => (b.ChampionId, b.TeamPosition)).Should().BeEquivalentTo(Allies);
        ally.Should().AllSatisfy(b =>
        {
            b.Games.Should().Be(12, "an ally shares the tracked player's game count");
            b.Wins.Should().Be(7, "and their result — teammates win and lose together");
        });
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

        (await db.ChampionSynergyStats.CountAsync()).Should().Be(Allies.Length);
        (await db.ChampionSynergyStats.AsNoTracking().MaxAsync(s => s.Games))
            .Should().Be(12, "counts must not double on a second run with nothing pending");
        (await db.Matches.CountAsync(m => !m.SynergyAggregated))
            .Should().Be(0, "every seeded match was folded in on the first run");
    }

    [Fact]
    public async Task RunAsync_AccumulatesAcrossBatchesAsNewMatchesArrive()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await SeedGamesAsync(games: 5, version: "16.4.521.123", wins: 2, matchPrefix: "m2");
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var pairs = await db.ChampionSynergyStats.AsNoTracking().ToListAsync();
        pairs.Should().HaveCount(Allies.Length, "the same keys must be updated, not duplicated");
        pairs.Should().AllSatisfy(pair =>
        {
            pair.Games.Should().Be(17, "the second batch adds to the first rather than replacing it");
            pair.Wins.Should().Be(9);
        });

        var self = await db.ChampionSynergyBaselineStats.AsNoTracking()
            .SingleAsync(b => b.Side == SynergyBaselineSide.Self);
        self.Games.Should().Be(17);
    }

    [Fact]
    public async Task RunAsync_FoldsMatchesWithoutAnIngestedTimeline()
    {
        await _fixture.ResetDatabaseAsync();
        // Synergy needs participant rows only, so — unlike the matchup fold — no
        // timeline gate applies and these matches must still be counted.
        await SeedGamesAsync(games: 6, version: "16.4.521.123", wins: 3, timelineIngested: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionSynergyStats.AsNoTracking().MaxAsync(s => (int?)s.Games)).Should().Be(6);
    }

    [Fact]
    public async Task RunAsync_IgnoresMatchesWhereNoParticipantIsTracked()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 8, version: "16.4.521.123", wins: 4, tracked: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        (await db.ChampionSynergyStats.CountAsync()).Should().Be(0, "the pairing question is asked from a tracked player's seat");
        (await db.ChampionSynergyBaselineStats.CountAsync()).Should().Be(0);
        (await db.Matches.CountAsync(m => !m.SynergyAggregated))
            .Should().Be(0, "an unproductive match is still flagged, or it would be re-read forever");
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
            mutate.MatchParticipants.RemoveRange(await mutate.MatchParticipants.ToListAsync());
            mutate.Matches.RemoveRange(await mutate.Matches.ToListAsync());
            await mutate.SaveChangesAsync();
        }

        await SeedGamesAsync(games: 11, version: "16.5.1", wins: 4, matchPrefix: "m2");
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var pairs = await db.ChampionSynergyStats.AsNoTracking().ToListAsync();

        pairs.Select(pair => pair.Patch).Distinct().Should().BeEquivalentTo(["16.4", "16.5"]);
        pairs.Where(pair => pair.Patch == "16.4").Should().AllSatisfy(pair => pair.Games.Should().Be(12));
        pairs.Where(pair => pair.Patch == "16.5").Should().AllSatisfy(pair => pair.Games.Should().Be(11));
    }

    private ChampionSynergyAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionSynergyAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new SynergyAggregationOptions()),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    /// <summary>
    /// Seeds full ten-player games: the tracked player on <see cref="Champion"/> at
    /// <see cref="Position"/>, the four <see cref="Allies"/> beside them, and the
    /// five <see cref="Enemies"/> on the other team.
    /// </summary>
    private async Task SeedGamesAsync(
        int games,
        string version,
        int wins,
        string matchPrefix = "m",
        bool tracked = true,
        bool timelineIngested = true)
    {
        await using var db = _fixture.CreateDbContext();

        // The account's Id is assigned client-side by the builder, so it is usable
        // for the participant rows below before SaveChanges.
        var account = await db.RiotAccounts.FirstOrDefaultAsync();
        if (account is null)
        {
            account = new RiotAccountBuilder()
                .WithGameName("SynergyMain")
                .WithTagLine("KR1")
                .WithPuuid("synergy-main-puuid")
                .Build();
            db.RiotAccounts.Add(account);
        }

        for (var i = 0; i < games; i++)
        {
            var matchId = $"{matchPrefix}-{version}-{i}";
            var matchBuilder = new MatchBuilder().WithId(matchId).WithQueueId(QueueId).WithGameVersion(version);
            if (timelineIngested)
            {
                matchBuilder = matchBuilder.WithTimelineIngested();
            }

            db.Matches.Add(matchBuilder.Build());

            var win = i < wins;
            var participantId = 1;

            db.MatchParticipants.Add(Participant(
                matchId, participantId++, Champion, Position, teamId: 100, win: win,
                riotAccountId: tracked ? account.Id : null));

            foreach (var (allyChampion, allyPosition) in Allies)
            {
                db.MatchParticipants.Add(Participant(
                    matchId, participantId++, allyChampion, allyPosition, teamId: 100, win: win));
            }

            foreach (var (enemyChampion, enemyPosition) in Enemies)
            {
                db.MatchParticipants.Add(Participant(
                    matchId, participantId++, enemyChampion, enemyPosition, teamId: 200, win: !win));
            }
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, string position, int teamId, bool win,
        Guid? riotAccountId = null)
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
            TeamPosition = position,
            IndividualPosition = position,
            Lane = position,
            Role = "SOLO",
            Win = win,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = []
        };
}
