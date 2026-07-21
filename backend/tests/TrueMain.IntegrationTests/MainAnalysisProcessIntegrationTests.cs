using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.MainAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MainAnalysisProcessIntegrationTests
{
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
    }

    private Task<object?> RunProcessAsync()
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
        int recentGameCount)
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
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 20, Rate = 1d }],
            CalculatedAtUtc = now.AddDays(-40)
        });

        for (var i = 0; i < recentGameCount; i++)
        {
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
