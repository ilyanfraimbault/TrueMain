using Data.Entities;
using Data.Repositories;
using FluentAssertions;
using Ingestor.Options;
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

        var result = calculator.Calculate("KR", "puuid-1", participants, options, DateTime.UtcNow);

        result.Should().HaveCount(2);

        var champion1 = result.Single(stat => stat.ChampionId == 1);
        champion1.TotalMatches.Should().Be(5);
        champion1.ChampionMatches.Should().Be(3);
        champion1.PlayRate.Should().BeApproximately(0.6, 0.0001);
        champion1.IsMain.Should().BeTrue();
        champion1.PrimaryPosition.Should().Be("TOP");

        var champion2 = result.Single(stat => stat.ChampionId == 2);
        champion2.ChampionMatches.Should().Be(2);
        champion2.PlayRate.Should().BeApproximately(0.4, 0.0001);
        champion2.IsMain.Should().BeFalse();
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
}
