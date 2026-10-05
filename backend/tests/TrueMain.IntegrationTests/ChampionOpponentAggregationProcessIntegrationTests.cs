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
/// Exercises the opposing-pair fold (#1713) against real Postgres: the two
/// <c>ON CONFLICT</c> upserts, the per-match flag that makes a re-run a no-op, and its
/// independence from the synergy flag.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionOpponentAggregationProcessIntegrationTests : IAsyncLifetime
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone, MIDDLE, the tracked side.
    private const string Position = "MIDDLE";
    private const string TrackedPuuid = "opponent-main-puuid";

    // The rest of the tracked player's team, one per remaining canonical lane.
    private static readonly (int ChampionId, string Position)[] Allies =
    [
        (86, "TOP"),      // Garen
        (64, "JUNGLE"),   // Lee Sin
        (81, "BOTTOM"),   // Ezreal
        (350, "UTILITY")  // Yuumi
    ];

    // The enemy team: one opposing pair each, the lane opponent included.
    private static readonly (int ChampionId, string Position)[] Enemies =
    [
        (122, "TOP"),
        (60, "JUNGLE"),
        (238, "MIDDLE"),
        (222, "BOTTOM"),
        (412, "UTILITY")
    ];

    private readonly PostgresFixture _fixture;

    public ChampionOpponentAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunAsync_FoldsOneRowPerEnemy_AndNoneForTeammates()
    {
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var pairs = await db.ChampionOpponentStats.AsNoTracking().ToListAsync();
        pairs.Should().HaveCount(Enemies.Length, "one row per enemy, the lane opponent included");
        pairs.Should().AllSatisfy(pair =>
        {
            pair.ChampionId.Should().Be(Champion);
            pair.TeamPosition.Should().Be(Position);
            pair.Patch.Should().Be("16.4");
            pair.Games.Should().Be(12);
            pair.Wins.Should().Be(7, "wins are the tracked player's");
        });

        pairs.Select(pair => (pair.OpponentChampionId, pair.OpponentPosition))
            .Should().BeEquivalentTo(Enemies);
        pairs.Select(pair => pair.OpponentChampionId)
            .Should().NotIntersectWith(Allies.Select(ally => ally.ChampionId));
    }

    [Fact]
    public async Task RunAsync_WritesSelfAndEnemyBaselinesFromTheTrackedSide()
    {
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var baselines = await db.ChampionOpponentBaselineStats.AsNoTracking().ToListAsync();

        var self = baselines.Where(b => b.Side == OpponentBaselineSide.Self).ToList();
        self.Should().ContainSingle();
        self[0].ChampionId.Should().Be(Champion);
        self[0].Games.Should().Be(12);
        self[0].Wins.Should().Be(7);

        var enemy = baselines.Where(b => b.Side == OpponentBaselineSide.Enemy).ToList();
        enemy.Select(b => (b.ChampionId, b.TeamPosition)).Should().BeEquivalentTo(Enemies);
        enemy.Should().AllSatisfy(b =>
        {
            b.Games.Should().Be(12);
            b.Wins.Should().Be(7, "the tracked player's wins against it, not the enemy's own");
        });
    }

    [Fact]
    public async Task RunAsync_DoesNotDoubleCountOnRerun_AndLeavesTheSynergyFlagAlone()
    {
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        (await db.ChampionOpponentStats.CountAsync()).Should().Be(Enemies.Length);
        (await db.ChampionOpponentStats.AsNoTracking().MaxAsync(s => s.Games)).Should().Be(12);
        (await db.Matches.CountAsync(m => !m.OpponentAggregated)).Should().Be(0);
        (await db.Matches.CountAsync(m => m.SynergyAggregated))
            .Should().Be(0, "the two folds have their own flags, so the backlog drains without refolding synergies");
    }

    [Fact]
    public async Task RunAsync_AccumulatesAcrossRunsAsNewMatchesArrive()
    {
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await SeedGamesAsync(games: 5, version: "16.4.521.123", wins: 2, matchPrefix: "m2");
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var pairs = await db.ChampionOpponentStats.AsNoTracking().ToListAsync();
        pairs.Should().HaveCount(Enemies.Length);
        pairs.Should().AllSatisfy(pair =>
        {
            pair.Games.Should().Be(17);
            pair.Wins.Should().Be(9);
        });
    }

    [Fact]
    public async Task RunAsync_IgnoresMatchesWhereNoParticipantIsTracked()
    {
        await SeedGamesAsync(games: 8, version: "16.4.521.123", wins: 4, tracked: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        (await db.ChampionOpponentStats.CountAsync()).Should().Be(0);
        (await db.ChampionOpponentBaselineStats.CountAsync()).Should().Be(0);
        (await db.Matches.CountAsync(m => !m.OpponentAggregated))
            .Should().Be(0, "an unproductive match is still flagged");
    }

    private ChampionOpponentAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionOpponentAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new OpponentAggregationOptions()),
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
                .WithGameName("OpponentMain")
                .WithTagLine("KR1")
                .WithPuuid(TrackedPuuid)
                .Build();
            db.RiotAccounts.Add(account);

            // The queried side of a pairing is a main of the champion it is playing
            // (ChampionCohort, #1365), not merely an account we track — so the cohort
            // row is part of the corpus, not decoration.
            db.MainChampionStats.Add(
                MainChampionStatSeed.Row(account.PlatformId, TrackedPuuid, Champion, Position));
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
                riotAccountId: tracked ? account.Id : null,
                puuid: tracked ? TrackedPuuid : null));

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
        Guid? riotAccountId = null, string? puuid = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            // The tracked seat carries the account's own puuid, because the cohort join
            // is on (platform, puuid, champion) — a per-row puuid would match no main.
            Puuid = puuid ?? $"puuid-{matchId}-{participantId}",
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
