using Core.Options;
using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.MainAnalysis;

namespace TrueMain.UnitTests;

public sealed class MainAnalysisComponentsTests
{
    [Fact]
    public void MainStatsCalculator_ComputesExpectedPlayRatesAndMainFlag()
    {
        var calculator = new MainStatsCalculator();
        var options = new MainAnalysisOptions
        {
            MinMatchesToEvaluate = 5,
            PlayRateThreshold = 0.6,
            OtpPlayRateThreshold = 0.8,
            CriticalPlayRateThreshold = 0.2
        };

        var participants = new List<ParticipantRow>
        {
            new(1, "TOP"),
            new(1, "TOP"),
            new(1, "JUNGLE"),
            new(2, "MID"),
            new(2, "MID"),
            new(1, "NONE")
        };

        var result = calculator.Calculate("KR", "puuid-1", participants, options, ChampionCoverageSnapshot.Empty, DateTime.UtcNow);

        result.Should().HaveCount(2);

        var champion1 = result.Single(stat => stat.ChampionId == 1);
        champion1.TotalMatches.Should().Be(5);
        champion1.ChampionMatches.Should().Be(3);
        champion1.PlayRate.Should().BeApproximately(0.6, 0.0001);
        champion1.IsMain.Should().BeTrue();
        champion1.IsOtp.Should().BeFalse();
        champion1.PrimaryPosition.Should().Be("TOP");

        var champion2 = result.Single(stat => stat.ChampionId == 2);
        champion2.ChampionMatches.Should().Be(2);
        champion2.PlayRate.Should().BeApproximately(0.4, 0.0001);
        champion2.IsMain.Should().BeFalse();
        champion2.IsOtp.Should().BeFalse();
    }

    [Fact]
    public void MainStatsCalculator_ComputesOtpFlag_WhenPlayRateExceedsOtpThreshold()
    {
        var calculator = new MainStatsCalculator();
        var options = new MainAnalysisOptions
        {
            MinMatchesToEvaluate = 5,
            PlayRateThreshold = 0.5,
            OtpPlayRateThreshold = 0.8
        };

        var participants = new List<ParticipantRow>
        {
            new(1, "TOP"),
            new(1, "TOP"),
            new(1, "TOP"),
            new(1, "MID"),
            new(1, "TOP"),
            new(2, "MID")
        };

        var result = calculator.Calculate("KR", "puuid-1", participants, options, ChampionCoverageSnapshot.Empty, DateTime.UtcNow);

        var champion1 = result.Single(stat => stat.ChampionId == 1);
        champion1.PlayRate.Should().BeApproximately(5d / 6d, 0.0001);
        champion1.IsMain.Should().BeTrue();
        champion1.IsOtp.Should().BeTrue();
    }

    [Fact]
    public void MainStatsCalculator_DoesNotSetOtp_WhenChampionIsNotMainEvenIfOtpThresholdIsLower()
    {
        var calculator = new MainStatsCalculator();
        var options = new MainAnalysisOptions
        {
            MinMatchesToEvaluate = 5,
            PlayRateThreshold = 0.8,
            OtpPlayRateThreshold = 0.5
        };

        var participants = new List<ParticipantRow>
        {
            new(1, "TOP"),
            new(1, "TOP"),
            new(1, "MID"),
            new(2, "MID"),
            new(2, "MID")
        };

        var result = calculator.Calculate("KR", "puuid-1", participants, options, ChampionCoverageSnapshot.Empty, DateTime.UtcNow);

        var champion1 = result.Single(stat => stat.ChampionId == 1);
        champion1.PlayRate.Should().BeApproximately(0.6, 0.0001);
        champion1.IsMain.Should().BeFalse();
        champion1.IsOtp.Should().BeFalse();
    }

    [Fact]
    public void MainDemotionPolicy_DemotesWhenExistingMainFallsBelowCriticalThreshold()
    {
        var policy = new MainDemotionPolicy();

        var existing = new List<MainChampionStat>
        {
            new() { ChampionId = 1, IsMain = true },
            new() { ChampionId = 2, IsMain = false }
        };

        var newStats = new Dictionary<int, MainChampionStat>
        {
            [1] = new() { ChampionId = 1, PlayRate = 0.09, IsMain = false }
        };

        var shouldDemote = policy.ShouldDemote(existing, newStats, 0.1);

        shouldDemote.Should().BeTrue();
    }

    [Fact]
    public void MainStatsCalculator_RelaxesThresholdAndFlagsExtendedSample_ForUnderCoveredChampion()
    {
        var calculator = new MainStatsCalculator();
        var options = new MainAnalysisOptions
        {
            MinMatchesToEvaluate = 5,
            PlayRateThreshold = 0.2,
            PlayRateFloor = 0.12,
            OtpPlayRateThreshold = 0.85
        };

        // Champion 1 is absent from the snapshot (no mains => the WHERE IsMain query omits it),
        // so its deficit is 1 and the threshold relaxes to the 0.12 floor.
        var coverage = new ChampionCoverageSnapshot(
            new Dictionary<int, int> { [2] = 30 },
            targetMainsPerChampion: 20);

        // Champion 1 is played 1/8 = 0.125: below base 0.2 but above the relaxed 0.12.
        var participants = new List<ParticipantRow>
        {
            new(1, "TOP"),
            new(2, "MID"), new(2, "MID"), new(2, "MID"),
            new(2, "MID"), new(2, "MID"), new(2, "MID"), new(2, "MID")
        };

        var result = calculator.Calculate("KR", "puuid-1", participants, options, coverage, DateTime.UtcNow);

        var champion1 = result.Single(stat => stat.ChampionId == 1);
        champion1.PlayRate.Should().BeApproximately(0.125, 0.0001);
        champion1.IsMain.Should().BeTrue();
        champion1.IsExtendedSample.Should().BeTrue();
        champion1.IsOtp.Should().BeFalse();
    }

    [Fact]
    public void MainStatsCalculator_KeepsBaseThreshold_ForCoveredChampion()
    {
        var calculator = new MainStatsCalculator();
        var options = new MainAnalysisOptions
        {
            MinMatchesToEvaluate = 5,
            PlayRateThreshold = 0.2,
            PlayRateFloor = 0.12,
            OtpPlayRateThreshold = 0.85
        };

        // Champion 1 is already at target (deficit = 0) => threshold stays at 0.2.
        var coverage = new ChampionCoverageSnapshot(
            new Dictionary<int, int> { [1] = 30, [2] = 30 },
            targetMainsPerChampion: 20);

        var participants = new List<ParticipantRow>
        {
            new(1, "TOP"),
            new(2, "MID"), new(2, "MID"), new(2, "MID"),
            new(2, "MID"), new(2, "MID"), new(2, "MID"), new(2, "MID")
        };

        var result = calculator.Calculate("KR", "puuid-1", participants, options, coverage, DateTime.UtcNow);

        var champion1 = result.Single(stat => stat.ChampionId == 1);
        champion1.PlayRate.Should().BeApproximately(0.125, 0.0001);
        champion1.IsMain.Should().BeFalse();
        champion1.IsExtendedSample.Should().BeFalse();
    }
}
