using Data.Entities;

namespace Ingestor.Processes.Components.MainAnalysis;

public interface IMainDemotionPolicy
{
    bool ShouldDemote(
        IReadOnlyCollection<MainChampionStat> existingStats,
        IReadOnlyDictionary<int, MainChampionStat> newStatsByChampion,
        double criticalPlayRateThreshold);
}
