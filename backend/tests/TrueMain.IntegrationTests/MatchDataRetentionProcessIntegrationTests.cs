using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MatchDataRetentionProcessIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public MatchDataRetentionProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldDeleteOutOfWindowRankedAndAllNonRankedMatches()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedRetentionDataAsync();

        var recorded = BuildRecordedProcess(retainedPatchCount: 1);

        await recorded.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var remainingMatchIds = await db.Matches
            .AsNoTracking()
            .OrderBy(match => match.Id)
            .Select(match => match.Id)
            .ToListAsync();

        // RET_1/RET_3: out-of-window ranked patches → pruned. RET_5: ARAM (450), a
        // non-ranked queue → drained even though it is on the newest patch. Only the
        // two in-window ranked matches survive.
        remainingMatchIds.Should().BeEquivalentTo(["RET_2", "RET_4"]);
        (await db.MatchParticipants.AsNoTracking().CountAsync()).Should().Be(2);
        (await db.ProcessRuns.AsNoTracking().AnyAsync(run => run.ProcessName == "MatchDataRetention")).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ShouldDrainNonRankedMatchesEvenWithNoRankedMatchesPresent()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using (var seedDb = _fixture.CreateDbContext())
        {
            seedDb.Matches.AddRange(
                BuildMatch("NR_1", "KR", now.AddHours(-2), "16.6.1", queueId: 450, gameMode: "ARAM", mapId: 12),
                BuildMatch("NR_2", "NA1", now.AddHours(-1), "16.6.1", queueId: 440));
            seedDb.MatchParticipants.AddRange(
                BuildParticipant(Guid.Parse("88888888-8888-8888-8888-8888888888a1"), "NR_1"),
                BuildParticipant(Guid.Parse("88888888-8888-8888-8888-8888888888a2"), "NR_2"));
            await seedDb.SaveChangesAsync();
        }

        var recorded = BuildRecordedProcess(retainedPatchCount: 2);

        await recorded.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.Matches.AsNoTracking().CountAsync()).Should().Be(0);
        (await db.MatchParticipants.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ShouldDeleteAggregatesForStalePatchesWhenEnabled()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregateDataAsync();

        var recorded = BuildRecordedProcess(retainedPatchCount: 2, aggregateRetainedPatchCount: 1);

        await recorded.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionAggregateScopes.AsNoTracking().Select(s => s.GameVersion).Distinct().ToListAsync())
            .Should().BeEquivalentTo(["16.5"]);
        (await db.ChampionAggregatePatterns.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.ChampionMatchupStats.AsNoTracking().Select(s => s.Patch).ToListAsync())
            .Should().BeEquivalentTo(["16.5"]);
        (await db.ChampionTimelineLeadStats.AsNoTracking().Select(s => s.Patch).ToListAsync())
            .Should().BeEquivalentTo(["16.5"]);
        (await db.ChampionPowerspikeCurveStats.AsNoTracking().Select(s => s.Patch).ToListAsync())
            .Should().BeEquivalentTo(["16.5"]);
        (await db.ChampionPowerspikeEventStats.AsNoTracking().Select(s => s.Patch).ToListAsync())
            .Should().BeEquivalentTo(["16.5"]);
    }

    [Fact]
    public async Task RunAsync_ShouldKeepAllAggregatesWhenAggregateRetentionDisabled()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregateDataAsync();

        var recorded = BuildRecordedProcess(retainedPatchCount: 1);

        await recorded.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionAggregateScopes.AsNoTracking().Select(s => s.GameVersion).Distinct().ToListAsync())
            .Should().BeEquivalentTo(["16.4", "16.5"]);
        (await db.ChampionAggregatePatterns.AsNoTracking().CountAsync()).Should().Be(2);
        (await db.ChampionMatchupStats.AsNoTracking().CountAsync()).Should().Be(2);
        (await db.ChampionTimelineLeadStats.AsNoTracking().CountAsync()).Should().Be(2);
        (await db.ChampionPowerspikeCurveStats.AsNoTracking().CountAsync()).Should().Be(2);
        (await db.ChampionPowerspikeEventStats.AsNoTracking().CountAsync()).Should().Be(2);
    }

    private async Task SeedAggregateDataAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new TrueMain.TestKit.EntityBuilders.RiotAccountBuilder()
            .WithGameName("RetentionMain")
            .WithTagLine("KR1")
            .WithPuuid("retention-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);
        await db.SaveChangesAsync();

        var aggregatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var seeder = new TrueMain.TestKit.EntityBuilders.ChampionAggregateSeeder();
        foreach (var patch in new[] { "16.4", "16.5" })
        {
            seeder.AddPatternDefaults(
                account.Id, championId: 22, patch, platformId: "KR", queueId: 420, position: "BOTTOM",
                summoner1Id: 4, summoner2Id: 7, skillOrderKey: "Q",
                buildItems: [6672], bootsItemId: 0, games: 5, wins: 3, aggregatedAt);

            db.ChampionMatchupStats.Add(new ChampionMatchupStat
            {
                Id = Guid.NewGuid(),
                ChampionId = 22,
                TeamPosition = "BOTTOM",
                OpponentChampionId = 51,
                Patch = patch,
                EloBracket = "GOLD",
                Games = 5,
                Wins = 3,
                AggregatedAtUtc = aggregatedAt
            });
            db.ChampionTimelineLeadStats.Add(new ChampionTimelineLeadStat
            {
                Id = Guid.NewGuid(),
                ChampionId = 22,
                TeamPosition = "BOTTOM",
                Patch = patch,
                IntervalMinute = 10,
                EloBracket = "GOLD",
                Games = 5,
                AggregatedAtUtc = aggregatedAt
            });
            db.ChampionPowerspikeCurveStats.Add(new ChampionPowerspikeCurveStat
            {
                Id = Guid.NewGuid(),
                ChampionId = 22,
                TeamPosition = "BOTTOM",
                Patch = patch,
                EloBracket = "GOLD",
                IntervalMinute = 10,
                Games = 5,
                AggregatedAtUtc = aggregatedAt
            });
            db.ChampionPowerspikeEventStats.Add(new ChampionPowerspikeEventStat
            {
                Id = Guid.NewGuid(),
                ChampionId = 22,
                TeamPosition = "BOTTOM",
                Patch = patch,
                EloBracket = "GOLD",
                EventType = "level",
                RefId = 6,
                Games = 5,
                AggregatedAtUtc = aggregatedAt
            });
        }

        await seeder.SaveAsync(db);
        await db.SaveChangesAsync();
    }

    private RecordedProcess<MatchDataRetentionProcess> BuildRecordedProcess(
        int retainedPatchCount,
        int aggregateRetainedPatchCount = 0)
    {
        var process = new MatchDataRetentionProcess(
            NullLogger<MatchDataRetentionProcess>.Instance,
            new TestDbContextFactory(_fixture),
            Microsoft.Extensions.Options.Options.Create(new MatchDataRetentionOptions
            {
                RetainedPatchCount = retainedPatchCount,
                NonRankedDeleteBatchSize = 1,
                AggregateRetainedPatchCount = aggregateRetainedPatchCount
            }),
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions
            {
                QueueId = LolQueueId.RankedSoloDuo
            }),
            Microsoft.Extensions.Options.Options.Create(new CandidatePruningOptions
            {
                Enabled = false
            }));

        return new RecordedProcess<MatchDataRetentionProcess>(
            process,
            new ProcessRunRecorder(_fixture.CreateSessionFactory(), new IterationContext()),
            TimeProvider.System,
            NullLogger<RecordedProcess<MatchDataRetentionProcess>>.Instance);
    }

    private async Task SeedRetentionDataAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.Matches.AddRange(
            BuildMatch("RET_1", "KR", now.AddDays(-10), "16.3.7"),
            BuildMatch("RET_2", "KR", now.AddDays(-1), "16.4.2"),
            BuildMatch("RET_3", "NA1", now.AddDays(-2), "16.4.1"),
            BuildMatch("RET_4", "NA1", now, "16.5.1"),
            BuildMatch("RET_5", "KR", now.AddHours(-1), "16.6.1", queueId: 450, gameMode: "ARAM", mapId: 12));

        db.MatchParticipants.AddRange(
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888881"), "RET_1"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888882"), "RET_2"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888883"), "RET_3"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888884"), "RET_4"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888885"), "RET_5"));

        await db.SaveChangesAsync();
    }

    private static Match BuildMatch(
        string id,
        string platformId,
        DateTime gameStartTimeUtc,
        string gameVersion,
        int queueId = 420,
        string gameMode = "CLASSIC",
        int mapId = 11)
    {
        return new Match
        {
            Id = id,
            PlatformId = platformId,
            QueueId = queueId,
            MapId = mapId,
            GameMode = gameMode,
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStartTimeUtc,
            GameDurationSeconds = 1800,
            GameVersion = gameVersion,
            CreatedAtUtc = gameStartTimeUtc,
            TimelineIngested = true
        };
    }

    private static MatchParticipant BuildParticipant(Guid id, string matchId)
    {
        return new MatchParticipant
        {
            Id = id,
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = $"{matchId}-puuid",
            SummonerName = $"{matchId}-puuid",
            SummonerLevel = 100,
            ChampionId = 22,
            TeamId = 100,
            TeamPosition = "BOTTOM",
            IndividualPosition = "BOTTOM",
            Lane = "BOTTOM",
            Role = "CARRY",
            Win = true,
            Kills = 1,
            Deaths = 1,
            Assists = 1,
            GoldEarned = 10000,
            TotalMinionsKilled = 100,
            NeutralMinionsKilled = 0,
            ChampLevel = 14,
            Item0 = 6672,
            Item1 = 3006,
            Item2 = 0,
            Item3 = 0,
            Item4 = 0,
            Item5 = 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5002,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8200,
            Summoner1Id = 4,
            Summoner2Id = 7,
            ItemEvents = [],
            SkillEvents = []
        };
    }

}
