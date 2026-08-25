using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.MainAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MainAnalysisProcessIntegrationTests
{
    // Raised by the trigger the rollback test installs on main_candidates, and
    // asserted on the exception it expects — one literal so the two cannot drift.
    private const string DemotionFailureMessage = "main_candidates update rejected by test trigger";

    private readonly PostgresFixture _fixture;

    public MainAnalysisProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldPersistReusableMainAndOtpClassification()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedValidatedAccountWithMatchesAsync();

        var process = new MainAnalysisProcess(
            NullLogger<MainAnalysisProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new MainStatsCalculator(),
            new MainDemotionPolicy(),
            new ChampionCoverageProvider(Microsoft.Extensions.Options.Options.Create(new CoverageOptions())),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions
            {
                BatchSize = 10,
                ProcessingBatchSize = 10,
                MatchesToConsider = 20,
                QueueId = LolQueueId.RankedSoloDuo,
                MinMatchesToEvaluate = 5,
                PlayRateThreshold = 0.5,
                OtpPlayRateThreshold = 0.8,
                CriticalPlayRateThreshold = 0.2,
                RecomputeAfterHours = 24
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.PlatformId == "KR" && a.Puuid == "puuid-main-1");
        var stats = verifyDb.MainChampionStats
            .Where(s => s.PlatformId == "KR" && s.Puuid == "puuid-main-1")
            .OrderBy(s => s.ChampionId)
            .ToList();

        account.LastMainCalcAtUtc.Should().NotBeNull();
        stats.Should().HaveCount(2);

        var otpStat = stats.Single(s => s.ChampionId == 22);
        otpStat.TotalMatches.Should().Be(10);
        otpStat.ChampionMatches.Should().Be(9);
        otpStat.PlayRate.Should().BeApproximately(0.9, 0.0001);
        otpStat.IsMain.Should().BeTrue();
        otpStat.IsOtp.Should().BeTrue();
        otpStat.PrimaryPosition.Should().Be("BOTTOM");

        var secondaryStat = stats.Single(s => s.ChampionId == 51);
        secondaryStat.ChampionMatches.Should().Be(1);
        secondaryStat.IsMain.Should().BeFalse();
        secondaryStat.IsOtp.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldFlagExtendedSample_ForUnderCoveredChampion()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedUnderCoveredScenarioAsync();

        var process = new MainAnalysisProcess(
            NullLogger<MainAnalysisProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new MainStatsCalculator(),
            new MainDemotionPolicy(),
            new ChampionCoverageProvider(
                Microsoft.Extensions.Options.Options.Create(new CoverageOptions { TargetMainsPerChampion = 20 })),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions
            {
                BatchSize = 10,
                ProcessingBatchSize = 10,
                MatchesToConsider = 20,
                QueueId = LolQueueId.RankedSoloDuo,
                MinMatchesToEvaluate = 5,
                PlayRateThreshold = 0.5,
                PlayRateFloor = 0.3,
                OtpPlayRateThreshold = 0.8,
                CriticalPlayRateThreshold = 0.2,
                RecomputeAfterHours = 24
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var stat = verifyDb.MainChampionStats
            .Single(s => s.PlatformId == "KR" && s.Puuid == "puuid-extended-1" && s.ChampionId == 22);

        // Champion 22 is absent from the (non-empty) coverage snapshot => deficit 1 => the
        // threshold relaxes from 0.5 to the 0.3 floor. A 0.4 play rate is below the base 0.5
        // bar, so the account is a main ONLY because of the relaxation, and is flagged extended.
        stat.PlayRate.Should().BeApproximately(0.4, 0.0001);
        stat.IsMain.Should().BeTrue();
        stat.IsExtendedSample.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ShouldRecomputeEstablishedMain_EvenWhenCandidateNotValidated()
    {
        // #825: an account whose candidate is stuck Queued (its own MatchIngestion
        // is backlogged) still keeps accruing passively-harvested recent games. It
        // must be re-analysed off the established-main eligibility path — otherwise
        // its displayed main freezes forever. Here the stale main (champ 100) has
        // no recent games and the account now plays champ 200 exclusively, so the
        // recompute must move the main to 200 and drop the stale 100.
        await _fixture.ResetDatabaseAsync();
        await SeedEstablishedMainAsync(
            puuid: "puuid-stale-1",
            candidateStatus: MainCandidateStatus.Queued,
            staleMainChampionId: 100,
            recentChampionId: 200,
            recentGameCount: 10);

        await RunProcessAsync();

        await using var verifyDb = _fixture.CreateDbContext();
        var stats = verifyDb.MainChampionStats
            .Where(s => s.PlatformId == "KR" && s.Puuid == "puuid-stale-1")
            .ToList();

        stats.Should().ContainSingle(s => s.ChampionId == 200 && s.IsMain,
            "the account's recent games are all on champion 200, so it becomes the new main");
        stats.Should().NotContain(s => s.ChampionId == 100,
            "the stale main has no recent games and is dropped from the delta");
    }

    [Fact]
    public async Task RunAsync_ShouldPreserveEstablishedMain_WhenRecentSampleTooSmall()
    {
        // #825 guard: the same passive-harvest path can surface an account with a
        // recent sample too small to classify (< MinMatchesToEvaluate). Applying
        // the delta would wipe the established main and drop the player off the
        // leaderboard on a sample we deem insufficient, so the main must be left
        // intact — but the calc timestamp is still stamped so the account waits a
        // full cycle before we retry.
        await _fixture.ResetDatabaseAsync();
        await SeedEstablishedMainAsync(
            puuid: "puuid-thin-1",
            candidateStatus: MainCandidateStatus.Queued,
            staleMainChampionId: 100,
            recentChampionId: 200,
            recentGameCount: 3); // below MinMatchesToEvaluate (5)

        await RunProcessAsync();

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.PlatformId == "KR" && a.Puuid == "puuid-thin-1");
        var stats = verifyDb.MainChampionStats
            .Where(s => s.PlatformId == "KR" && s.Puuid == "puuid-thin-1")
            .ToList();

        stats.Should().ContainSingle(s => s.ChampionId == 100 && s.IsMain,
            "a sample below MinMatchesToEvaluate must not wipe the established main");
        stats.Should().NotContain(s => s.ChampionId == 200,
            "the thin recent sample must not be persisted over the established main");
        account.LastMainCalcAtUtc.Should().NotBeNull("the account is still stamped so it waits a full recompute cycle");
        stats.Should().OnlyContain(s => !s.IsSampleRetired,
            "three recent games is a thin sample, not an absent one — the figures still describe games we hold");
    }

    [Fact]
    public async Task RunAsync_ShouldFlagSampleRetired_WhenTheAccountHasNoMatchesLeft()
    {
        // #1216: raw matches age out of MatchDataRetention (two patches in prod), so an
        // account nobody re-ingested recomputes to zero participants. That is not the thin
        // sample #825 protects against — the evidence is gone, not merely weak — and left
        // unflagged the guard above holds forever, leaving the row asserting a game count
        // nothing can corroborate. The row must survive (deleting it would drop the player
        // off the leaderboard the moment their matches expire) but be marked.
        await _fixture.ResetDatabaseAsync();
        await SeedEstablishedMainAsync(
            puuid: "puuid-retired-1",
            candidateStatus: MainCandidateStatus.Queued,
            staleMainChampionId: 100,
            recentChampionId: 200,
            recentGameCount: 0);

        await RunProcessAsync();

        await using var verifyDb = _fixture.CreateDbContext();
        var stats = verifyDb.MainChampionStats
            .Where(s => s.PlatformId == "KR" && s.Puuid == "puuid-retired-1")
            .ToList();

        stats.Should().ContainSingle(s => s.ChampionId == 100 && s.IsMain,
            "the row is flagged, never deleted — the player stays on the leaderboard");
        stats.Should().OnlyContain(s => s.IsSampleRetired,
            "zero participants means the sample these figures describe no longer exists");
    }

    [Fact]
    public async Task RunAsync_ShouldClearSampleRetired_WhenGamesComeBack()
    {
        // The flag is self-clearing: an account that gets re-ingested must be trusted
        // again on the very cycle that sees real games, without waiting for anything else.
        await _fixture.ResetDatabaseAsync();
        await SeedEstablishedMainAsync(
            puuid: "puuid-restored-1",
            candidateStatus: MainCandidateStatus.Queued,
            staleMainChampionId: 100,
            recentChampionId: 200,
            recentGameCount: 10,
            staleSampleRetired: true);

        await RunProcessAsync();

        await using var verifyDb = _fixture.CreateDbContext();
        var stats = verifyDb.MainChampionStats
            .Where(s => s.PlatformId == "KR" && s.Puuid == "puuid-restored-1")
            .ToList();

        stats.Should().NotBeEmpty();
        stats.Should().OnlyContain(s => !s.IsSampleRetired,
            "a recompute from a real sample clears the flag an earlier zero cycle set");
    }

    [Fact]
    public async Task RunAsync_ShouldKeepSampleRetired_WhenGamesComeBackBelowTheEvaluationFloor()
    {
        // The subtle half of #1216. A retired account that gets a couple of matches
        // re-ingested — still under MinMatchesToEvaluate — takes the #825 early return,
        // which deliberately leaves ChampionMatches / PlayRate / CalculatedAtUtc frozen.
        // Clearing the flag there would put the untouched, weeks-old count back on the
        // profile as an undated current number: the original bug, on a narrower
        // threshold. Only a real recompute (UpsertChampionStats) may un-retire a row.
        await _fixture.ResetDatabaseAsync();
        await SeedEstablishedMainAsync(
            puuid: "puuid-kept-1",
            candidateStatus: MainCandidateStatus.Queued,
            staleMainChampionId: 100,
            recentChampionId: 200,
            recentGameCount: 3, // nonzero, but below MinMatchesToEvaluate (5)
            staleSampleRetired: true);

        await RunProcessAsync();

        await using var verifyDb = _fixture.CreateDbContext();
        var stats = verifyDb.MainChampionStats
            .Where(s => s.PlatformId == "KR" && s.Puuid == "puuid-kept-1")
            .ToList();

        var main = stats.Should().ContainSingle(s => s.ChampionId == 100).Subject;
        main.IsSampleRetired.Should().BeTrue(
            "the guard never refreshed the figures, so they still describe games we no longer hold");
        main.ChampionMatches.Should().Be(20,
            "the guard leaves the established main's figures untouched — which is exactly why the flag must stay");
    }

    [Fact]
    public async Task RunAsync_ShouldRollBackStatWrites_WhenDemotionFails()
    {
        // #264 narrowed the transaction so it wraps only the writes. The stat delta,
        // the LastMainCalcAtUtc stamp and the candidate demotion must still commit —
        // or roll back — as a single unit: a failing demotion may not leave the stat
        // writes committed on their own.
        await _fixture.ResetDatabaseAsync();
        await SeedEstablishedMainAsync(
            puuid: "puuid-rollback-1",
            candidateStatus: MainCandidateStatus.Validated,
            staleMainChampionId: 100,
            recentChampionId: 200,
            recentGameCount: 10);

        DateTime? stampBeforeRun;
        await using (var beforeDb = _fixture.CreateDbContext())
        {
            stampBeforeRun = beforeDb.RiotAccounts
                .Single(a => a.PlatformId == "KR" && a.Puuid == "puuid-rollback-1")
                .LastMainCalcAtUtc;
        }

        // The established main (100) has no recent games, so the demotion policy
        // fires and the batch reaches the demotion ExecuteUpdate. The candidate is
        // Validated, so that statement matches its row — and this trigger makes it
        // fail inside the write transaction, after SaveChangesAsync has already
        // flushed the stat delta and the timestamp stamp.
        await ExecuteSqlAsync(
            $"""
            CREATE OR REPLACE FUNCTION fail_main_candidate_update() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION '{DemotionFailureMessage}';
            END;
            $$ LANGUAGE plpgsql;
            """);
        await ExecuteSqlAsync(
            """
            CREATE TRIGGER fail_main_candidate_update
            BEFORE UPDATE ON main_candidates
            FOR EACH ROW EXECUTE FUNCTION fail_main_candidate_update();
            """);

        try
        {
            // Pin the exact failure: the demotion goes through ExecuteUpdate (raw
            // SQL), not SaveChanges, so the trigger surfaces as a bare
            // PostgresException — never wrapped in a DbUpdateException. Asserting
            // the SQLSTATE and the message ties this to *our* trigger: with a broad
            // Exception the test would still go green if the run blew up for an
            // unrelated reason, because the rollback assertions below hold trivially
            // when nothing was written in the first place.
            var run = () => RunProcessAsync();
            var thrown = await run.Should().ThrowAsync<PostgresException>(
                "the demotion statement fails inside the narrowed write transaction");
            thrown.Which.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
            thrown.Which.MessageText.Should().Be(DemotionFailureMessage);

            await using var verifyDb = _fixture.CreateDbContext();
            var stats = verifyDb.MainChampionStats
                .Where(s => s.PlatformId == "KR" && s.Puuid == "puuid-rollback-1")
                .ToList();
            var account = verifyDb.RiotAccounts.Single(a => a.PlatformId == "KR" && a.Puuid == "puuid-rollback-1");

            stats.Should().ContainSingle(s => s.ChampionId == 100 && s.IsMain,
                "the rolled-back transaction must leave the established main untouched");
            stats.Should().NotContain(s => s.ChampionId == 200,
                "the recomputed stat row must not survive a failed demotion");
            account.LastMainCalcAtUtc.Should().Be(stampBeforeRun,
                "the LastMainCalcAtUtc stamp belongs to the same transaction as the demotion");
        }
        finally
        {
            await ExecuteSqlAsync("DROP TRIGGER IF EXISTS fail_main_candidate_update ON main_candidates;");
            await ExecuteSqlAsync("DROP FUNCTION IF EXISTS fail_main_candidate_update();");
        }
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var db = _fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private Task<IProcessRunSummary?> RunProcessAsync()
    {
        var process = new MainAnalysisProcess(
            NullLogger<MainAnalysisProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new MainStatsCalculator(),
            new MainDemotionPolicy(),
            new ChampionCoverageProvider(Microsoft.Extensions.Options.Options.Create(new CoverageOptions())),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions
            {
                BatchSize = 10,
                ProcessingBatchSize = 10,
                MatchesToConsider = 20,
                QueueId = LolQueueId.RankedSoloDuo,
                MinMatchesToEvaluate = 5,
                PlayRateThreshold = 0.5,
                OtpPlayRateThreshold = 0.8,
                CriticalPlayRateThreshold = 0.2,
                RecomputeAfterHours = 24
            }));

        return process.RunCoreAsync(CancellationToken.None);
    }

    private async Task SeedEstablishedMainAsync(
        string puuid,
        MainCandidateStatus candidateStatus,
        int staleMainChampionId,
        int recentChampionId,
        int recentGameCount,
        bool staleSampleRetired = false)
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = puuid,
            GameName = "stale-main-player",
            PlatformId = "KR",
            SummonerId = $"summoner-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 200,
            CreatedAtUtc = now.AddDays(-60),
            UpdatedAtUtc = now.AddDays(-1),
            // Old enough that the RecomputeAfterHours cutoff always re-selects it.
            LastMainCalcAtUtc = now.AddDays(-40)
        });

        db.MainCandidates.Add(new MainCandidate
        {
            PlatformId = "KR",
            Puuid = puuid,
            ChampionId = staleMainChampionId,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 800_000,
            LastPlayTimeUtc = now.AddDays(-1),
            DiscoveredAtUtc = now.AddDays(-50),
            Score = 90,
            Status = candidateStatus,
            ScoredAtUtc = now.AddDays(-50)
        });

        // The established (now stale) main: IsMain=true with no recent games on it.
        db.MainChampionStats.Add(new MainChampionStat
        {
            PlatformId = "KR",
            Puuid = puuid,
            ChampionId = staleMainChampionId,
            TotalMatches = 20,
            ChampionMatches = 20,
            PlayRate = 1d,
            IsMain = true,
            IsOtp = true,
            IsExtendedSample = false,
            IsSampleRetired = staleSampleRetired,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 20, Rate = 1d }],
            CalculatedAtUtc = now.AddDays(-40)
        });

        for (var i = 0; i < recentGameCount; i++)
        {
            // Match.Id is varchar(32), so keep test puuids short — "KR_RECENT_" plus the
            // puuid plus the index has to fit, which caps the puuid at ~20 characters.
            var matchId = $"KR_RECENT_{puuid}_{i}";
            db.Matches.Add(new Match
            {
                Id = matchId,
                PlatformId = "KR",
                QueueId = (int)LolQueueId.RankedSoloDuo,
                MapId = (int)LolMapId.SummonersRift,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-i),
                GameDurationSeconds = 1800,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddHours(-i),
                TimelineIngested = true
            });

            db.MatchParticipants.Add(new MatchParticipant
            {
                MatchId = matchId,
                ParticipantId = 1,
                Puuid = puuid,
                SummonerName = "stale-main-player",
                SummonerLevel = 200,
                ChampionId = recentChampionId,
                TeamId = 100,
                TeamPosition = "MIDDLE",
                IndividualPosition = "MIDDLE",
                Lane = "MIDDLE",
                Role = "SOLO",
                Win = i % 2 == 0,
                Kills = 6,
                Deaths = 3,
                Assists = 8,
                GoldEarned = 12000,
                TotalMinionsKilled = 190,
                NeutralMinionsKilled = 6,
                ChampLevel = 16,
                Item0 = 6655,
                Item1 = 3020,
                Item2 = 3157,
                Item3 = 3089,
                Item4 = 3135,
                Item5 = 3116,
                Item6 = 3363,
                TrinketItemId = 3363,
                PerksDefense = 5001,
                PerksFlex = 5008,
                PerksOffense = 5005,
                PrimaryStyleId = 8200,
                SubStyleId = 8100,
                Summoner1Id = 4,
                Summoner2Id = 14,
                ItemEvents = [],
                SkillEvents = []
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedValidatedAccountWithMatchesAsync()
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = "puuid-main-1",
            GameName = "main-player",
            PlatformId = "KR",
            SummonerId = "summoner-main-1",
            ProfileIconId = 1,
            SummonerLevel = 200,
            CreatedAtUtc = now.AddDays(-30),
            UpdatedAtUtc = now.AddDays(-1)
        });

        db.MainCandidates.Add(new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-main-1",
            ChampionId = 22,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 900_000,
            LastPlayTimeUtc = now.AddDays(-1),
            DiscoveredAtUtc = now.AddDays(-2),
            Score = 92,
            Status = MainCandidateStatus.Validated,
            ScoredAtUtc = now.AddDays(-2),
            ValidatedAtUtc = now.AddDays(-1)
        });

        for (var i = 0; i < 10; i++)
        {
            var matchId = $"KR_MAIN_{i}";
            var championId = i < 9 ? 22 : 51;

            db.Matches.Add(new Match
            {
                Id = matchId,
                PlatformId = "KR",
                QueueId = (int)LolQueueId.RankedSoloDuo,
                MapId = (int)LolMapId.SummonersRift,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-i),
                GameDurationSeconds = 1800,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddHours(-i),
                TimelineIngested = true
            });

            db.MatchParticipants.Add(new MatchParticipant
            {
                MatchId = matchId,
                ParticipantId = 1,
                Puuid = "puuid-main-1",
                SummonerName = "main-player",
                SummonerLevel = 200,
                ChampionId = championId,
                TeamId = 100,
                TeamPosition = "BOTTOM",
                IndividualPosition = "BOTTOM",
                Lane = "BOTTOM",
                Role = "DUO_CARRY",
                Win = i < 7,
                Kills = 5 + i,
                Deaths = 2,
                Assists = 7,
                GoldEarned = 12000 + i,
                TotalMinionsKilled = 200,
                NeutralMinionsKilled = 4,
                ChampLevel = 15,
                Item0 = 6672,
                Item1 = 3006,
                Item2 = 3085,
                Item3 = 3031,
                Item4 = 3036,
                Item5 = 3094,
                Item6 = 3363,
                TrinketItemId = 3363,
                PerksDefense = 5001,
                PerksFlex = 5008,
                PerksOffense = 5005,
                PrimaryStyleId = 8000,
                SubStyleId = 8100,
                Summoner1Id = 4,
                Summoner2Id = 7,
                ItemEvents = [],
                SkillEvents = []
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedUnderCoveredScenarioAsync()
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        // A covered champion (99) already has a main, so the coverage snapshot is non-empty.
        // No RiotAccount/candidate is attached, so this row is never re-analysed — it just
        // makes champion 22 (played by the test account) absent from the snapshot => deficit 1.
        db.MainChampionStats.Add(new MainChampionStat
        {
            PlatformId = "KR",
            Puuid = "puuid-covered-1",
            ChampionId = 99,
            TotalMatches = 30,
            ChampionMatches = 28,
            PlayRate = 28d / 30d,
            IsMain = true,
            IsOtp = true,
            IsExtendedSample = false,
            PrimaryPosition = "TOP",
            PositionBreakdown = [],
            CalculatedAtUtc = now.AddDays(-1)
        });

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = "puuid-extended-1",
            GameName = "extended-player",
            PlatformId = "KR",
            SummonerId = "summoner-extended-1",
            ProfileIconId = 1,
            SummonerLevel = 150,
            CreatedAtUtc = now.AddDays(-30),
            UpdatedAtUtc = now.AddDays(-1)
        });

        db.MainCandidates.Add(new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-extended-1",
            ChampionId = 22,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 500_000,
            LastPlayTimeUtc = now.AddDays(-1),
            DiscoveredAtUtc = now.AddDays(-2),
            Score = 80,
            Status = MainCandidateStatus.Validated,
            ScoredAtUtc = now.AddDays(-2),
            ValidatedAtUtc = now.AddDays(-1)
        });

        // 4 of 10 games on champion 22 => 0.4 play rate (between the 0.3 floor and 0.5 base).
        for (var i = 0; i < 10; i++)
        {
            var matchId = $"KR_EXT_{i}";
            var championId = i < 4 ? 22 : 51;

            db.Matches.Add(new Match
            {
                Id = matchId,
                PlatformId = "KR",
                QueueId = (int)LolQueueId.RankedSoloDuo,
                MapId = (int)LolMapId.SummonersRift,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-i),
                GameDurationSeconds = 1800,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddHours(-i),
                TimelineIngested = true
            });

            db.MatchParticipants.Add(new MatchParticipant
            {
                MatchId = matchId,
                ParticipantId = 1,
                Puuid = "puuid-extended-1",
                SummonerName = "extended-player",
                SummonerLevel = 150,
                ChampionId = championId,
                TeamId = 100,
                TeamPosition = "MIDDLE",
                IndividualPosition = "MIDDLE",
                Lane = "MIDDLE",
                Role = "SOLO",
                Win = i % 2 == 0,
                Kills = 6,
                Deaths = 3,
                Assists = 8,
                GoldEarned = 12000,
                TotalMinionsKilled = 180,
                NeutralMinionsKilled = 8,
                ChampLevel = 16,
                Item0 = 6655,
                Item1 = 3020,
                Item2 = 3157,
                Item3 = 3089,
                Item4 = 3135,
                Item5 = 3116,
                Item6 = 3363,
                TrinketItemId = 3363,
                PerksDefense = 5001,
                PerksFlex = 5008,
                PerksOffense = 5005,
                PrimaryStyleId = 8200,
                SubStyleId = 8100,
                Summoner1Id = 4,
                Summoner2Id = 14,
                ItemEvents = [],
                SkillEvents = []
            });
        }

        await db.SaveChangesAsync();
    }
}
