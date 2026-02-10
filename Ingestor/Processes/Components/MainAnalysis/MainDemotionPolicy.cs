using Data.Entities;

namespace Ingestor.Processes.Components.MainAnalysis;

public sealed class MainDemotionPolicy : IMainDemotionPolicy
{
    public bool ShouldDemote(
        IReadOnlyCollection<MainChampionStat> existingStats,
        IReadOnlyDictionary<int, MainChampionStat> newStatsByChampion,
        double criticalPlayRateThreshold)
    {
        return existingStats
            .Where(stat => stat.IsMain)
            .Any(stat => !newStatsByChampion.TryGetValue(stat.ChampionId, out var newStat)
                         || newStat.PlayRate < criticalPlayRateThreshold);
    }
}
