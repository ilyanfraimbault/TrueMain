using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.Aggregation;
using Data.BuildFacts;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.PatternAggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The acceptance test of #1365: one corpus, five folds, one denominator.
///
/// <para>
/// A champion page stacks a header, a matchups panel, a synergies panel and a power
/// spikes panel, and a reader compares their game counts by eye. They are written by
/// different processes, and every time one of them restated the cohort in its own words
/// they diverged while still looking comparable — 3.2x on production when the matchup
/// folds gated on "an account we know" instead of "a main of this champion" (#1087),
/// and the same defect survived on synergies and power spikes until #1365.
/// </para>
///
/// <para>
/// The corpus is built to catch exactly that: the same champion, in the same lane, on
/// the same patch, played by a <b>main</b> and by a <b>tracked non-main</b> — plus a
/// remake. Only the main's games may be counted, by all four folds and by the header
/// above them.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionCohortAcrossFoldsIntegrationTests
{
    private const int QueueId = 420;
    private const string Platform = "KR";
    private const string Version = "16.4.521.123";
    private const string Patch = "16.4";
    private const int Champion = 157;  // Yone, the champion whose page this is.
    private const int Opponent = 238;  // Zed, the MIDDLE opponent.
    private const string Position = "MIDDLE";

    private const string MainPuuid = "cohort-main-puuid";
    private const string NonMainPuuid = "cohort-non-main-puuid";

    private const int MainGames = 6;
    private const int NonMainGames = 3;
    private const int RemakeGames = 2;

    // The rest of the main's team: one per remaining canonical lane, so the synergy
    // fold has four partners to pair with.
    private static readonly (int ChampionId, string Position)[] Allies =
    [
        (86, "TOP"), (64, "JUNGLE"), (81, "BOTTOM"), (350, "UTILITY")
    ];

    private static readonly (int ChampionId, string Position)[] Enemies =
    [
        (122, "TOP"), (60, "JUNGLE"), (Opponent, Position), (222, "BOTTOM"), (412, "UTILITY")
    ];

    private readonly PostgresFixture _fixture;

    public ChampionCohortAcrossFoldsIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Every_panel_on_the_champion_page_counts_the_same_games()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedCorpusAsync();

        await RunEveryFoldAsync();

        await using var db = _fixture.CreateDbContext();

        // The header: champion_aggregate_scopes, the truemains population the page
        // opens on.
        var headerGames = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.ChampionId == Champion && scope.IsMain)
            .SumAsync(scope => scope.Games);

        // The matchups panel: one row per opponent, summed back to the champion's
        // games on this lane and patch.
        var matchupGames = await db.ChampionMatchupStats
            .AsNoTracking()
            .Where(row => row.ChampionId == Champion && row.TeamPosition == Position && row.Patch == Patch)
            .SumAsync(row => row.Games);

        // The lane counters folded onto those same rows by the second matchup fold.
        var laneGames = await db.ChampionMatchupStats
            .AsNoTracking()
            .Where(row => row.ChampionId == Champion && row.TeamPosition == Position && row.Patch == Patch)
            .SumAsync(row => row.LaneGames);

        // The synergies panel: the SELF baseline is exactly "the games the queried
        // side played", which is why the read divides by it.
        var synergyGames = await db.ChampionSynergyBaselineStats
            .AsNoTracking()
            .Where(row => row.ChampionId == Champion
                && row.TeamPosition == Position
                && row.Side == SynergyBaselineSide.Self
                && row.Patch == Patch)
            .SumAsync(row => row.Games);

        // The power spikes panel: the curve's sample at a minute every seeded game
        // reached.
        var powerspikeGames = await db.ChampionPowerspikeCurveStats
            .AsNoTracking()
            .Where(row => row.ChampionId == Champion
                && row.TeamPosition == Position
                && row.Patch == Patch
                && row.IntervalMinute == 10)
            .SumAsync(row => row.Games);

        headerGames.Should().Be(MainGames, "the corpus holds exactly this many games by a main of the champion");
        matchupGames.Should().Be(MainGames);
        laneGames.Should().Be(MainGames);
        synergyGames.Should().Be(MainGames, "before #1365 this counted the tracked non-main's games too");
        powerspikeGames.Should().Be(MainGames, "before #1365 this counted the tracked non-main's games too");
    }

    [Fact]
    public async Task No_fold_counts_a_remake()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedCorpusAsync();

        await RunEveryFoldAsync();

        await using var db = _fixture.CreateDbContext();

        // Every remade game was still flagged as folded, or the folds would re-read
        // them on every cycle for ever.
        (await db.Matches.CountAsync(m => m.Id.StartsWith("KR_REMAKE")))
            .Should().Be(RemakeGames);
        (await db.Matches.CountAsync(m => m.Id.StartsWith("KR_REMAKE")
            && m.SynergyAggregated && m.PowerspikeAggregated
            && m.MatchupLeadAggregated && m.LaneOutcomeAggregated))
            .Should().Be(RemakeGames, "an unproductive match is still flagged, or it is re-read forever");

        // …and contributed nothing. MainGames alone would also hold if remakes were
        // counted and the non-main was not, hence the separate assertion above.
        (await db.ChampionSynergyBaselineStats.AsNoTracking()
            .Where(row => row.Side == SynergyBaselineSide.Self)
            .SumAsync(row => row.Games))
            .Should().Be(MainGames);
    }

    private async Task RunEveryFoldAsync()
    {
        var factory = new TestDbContextFactory(_fixture);

        await new ChampionMatchupLeadAggregationProcess(
            NullLogger<ChampionMatchupLeadAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(AnalysisOptions()),
            Microsoft.Extensions.Options.Options.Create(new MatchupLeadAggregationOptions()),
            factory,
            TimeProvider.System).RunCoreAsync(CancellationToken.None);

        await new ChampionLaneOutcomeAggregationProcess(
            NullLogger<ChampionLaneOutcomeAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(AnalysisOptions()),
            Microsoft.Extensions.Options.Options.Create(new LaneOutcomeAggregationOptions()),
            factory,
            TimeProvider.System).RunCoreAsync(CancellationToken.None);

        await new ChampionSynergyAggregationProcess(
            NullLogger<ChampionSynergyAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(AnalysisOptions()),
            Microsoft.Extensions.Options.Options.Create(new SynergyAggregationOptions()),
            factory,
            TimeProvider.System).RunCoreAsync(CancellationToken.None);

        await new ChampionPowerspikeAggregationProcess(
            NullLogger<ChampionPowerspikeAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new PowerspikeAggregationOptions()),
            Microsoft.Extensions.Options.Options.Create(AnalysisOptions()),
            factory,
            new CohortItemMetadataProvider(),
            TimeProvider.System).RunCoreAsync(CancellationToken.None);

        await new ChampionPatternAggregationProcess(
            NullLogger<ChampionPatternAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(AnalysisOptions()),
            new ChampionPatternSourceRowReader(factory),
            new ChampionPatternAggregateBuilder(new CohortItemMetadataProvider()),
            new ChampionPatternAggregatePersister(factory, new ChampionDimensionResolver(factory)),
            TimeProvider.System).RunCoreAsync(CancellationToken.None);
    }

    private static MainAnalysisOptions AnalysisOptions()
        => new() { QueueId = LolQueueId.RankedSoloDuo };

    private async Task SeedCorpusAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var main = new RiotAccountBuilder()
            .WithPlatformId(Platform).WithPuuid(MainPuuid)
            .WithGameName("CohortMain").WithTagLine("KR1").Build();
        var nonMain = new RiotAccountBuilder()
            .WithPlatformId(Platform).WithPuuid(NonMainPuuid)
            .WithGameName("CohortNonMain").WithTagLine("KR1").Build();
        db.RiotAccounts.AddRange(main, nonMain);

        // Both accounts are tracked and both play the champion. Only one of them mains
        // it — the whole point of the corpus.
        db.MainChampionStats.Add(MainChampionStatSeed.Row(Platform, MainPuuid, Champion, Position));
        db.MainChampionStats.Add(
            MainChampionStatSeed.Row(Platform, NonMainPuuid, Champion, Position, isMain: false));

        for (var i = 0; i < MainGames; i++)
        {
            SeedGame(db, $"KR_MAIN_{i}", MainPuuid, main.Id, durationSeconds: 1800, win: i % 2 == 0);
        }

        for (var i = 0; i < NonMainGames; i++)
        {
            SeedGame(db, $"KR_NONMAIN_{i}", NonMainPuuid, nonMain.Id, durationSeconds: 1800, win: true);
        }

        for (var i = 0; i < RemakeGames; i++)
        {
            // Under ChampionCohort.MinimumGameDurationSeconds: a remake, played by the
            // main, on the same champion and lane. Nothing may count it.
            SeedGame(db, $"KR_REMAKE_{i}", MainPuuid, main.Id, durationSeconds: 240, win: false);
        }

        await db.SaveChangesAsync();
    }

    private static void SeedGame(
        Data.TrueMainDbContext db,
        string matchId,
        string puuid,
        Guid riotAccountId,
        int durationSeconds,
        bool win)
    {
        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithPlatformId(Platform)
            .WithQueueId(QueueId)
            .WithGameVersion(Version)
            .WithGameDurationSeconds(durationSeconds)
            .WithTimelineIngested()
            .Build());

        var participantId = 1;
        db.MatchParticipants.Add(Participant(
            matchId, participantId++, Champion, Position, teamId: 100, win: win,
            puuid: puuid, riotAccountId: riotAccountId));

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

        // The lane pair's per-minute grid: the champion side is slot 1, its MIDDLE
        // opponent is slot 8 (the third enemy seeded). Both are needed — the power
        // curve is the difference between them, and the lane outcome reads minute 15.
        const int championSlot = 1;
        var opponentSlot = 1 + Allies.Length + 1 + Array.FindIndex(Enemies, e => e.Position == Position);
        for (var minute = 1; minute <= 30; minute++)
        {
            db.MatchParticipantTimelineSnapshots.Add(
                Snapshot(matchId, championSlot, minute, 10_000 + minute * 100, 5_000 + minute * 50));
            db.MatchParticipantTimelineSnapshots.Add(
                Snapshot(matchId, opponentSlot, minute, 9_000 + minute * 100, 4_000 + minute * 50));
        }
    }

    private static MatchParticipantTimelineSnapshot Snapshot(
        string matchId, int participantId, int minute, int gold, int damage)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = minute,
            TotalGold = gold,
            TimestampMs = minute * 60_000,
            Xp = minute * 300,
            Level = Math.Min(18, 1 + minute / 2),
            MinionsKilled = minute * 6,
            JungleMinionsKilled = 0,
            DamageToChampions = damage
        };

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, string position, int teamId, bool win,
        string? puuid = null, Guid? riotAccountId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = participantId,
            // The cohort join is on (platform, puuid, champion), so the tracked seats
            // carry their account's own puuid.
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
            Kills = 5,
            Deaths = 3,
            Assists = 7,
            GoldEarned = 12_000,
            TotalMinionsKilled = 180,
            NeutralMinionsKilled = 0,
            ChampLevel = 16,
            Item0 = 6672,
            Item1 = 3006,
            Item6 = 3363,
            TrinketItemId = 3363,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 4,
            Summoner2Id = 7,
            // The header's own floor: a scope is only folded from a game with a
            // correlated timeline (purchases and at least three skill level-ups).
            ItemEvents =
            [
                new ItemEvent { TimestampMs = 60_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 600_000, ItemId = 6672, EventType = "ITEM_PURCHASED" }
            ],
            SkillEvents =
            [
                new SkillEvent { TimestampMs = 60_000, SkillSlot = 1, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 120_000, SkillSlot = 2, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 180_000, SkillSlot = 3, LevelUpType = "NORMAL" }
            ]
        };

    private sealed class CohortItemMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Metadata = new Dictionary<int, ItemMetadata>
        {
            [3006] = new(3006, 1100, true, false, true, false, true, true),
            [6672] = new(6672, 3000, true, false, false, false, true, false)
        };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Metadata);
    }
}
