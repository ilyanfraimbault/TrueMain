using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using FluentAssertions;
using Ingestor.Processes;
using Ingestor.Processes.Components.MainAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

public sealed class MainAnalysisProcessIntegrationTests : IClassFixture<PostgresFixture>
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
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions
            {
                BatchSize = 10,
                ProcessingBatchSize = 10,
                MatchesToConsider = 20,
                QueueId = LolQueueIds.RankedSoloDuo,
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
                QueueId = LolQueueIds.RankedSoloDuo,
                MapId = LolMapIds.SummonersRift,
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

}
