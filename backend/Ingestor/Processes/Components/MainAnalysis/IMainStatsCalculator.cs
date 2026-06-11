using Core.Options;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.Coverage;

namespace Ingestor.Processes.Components.MainAnalysis;

public interface IMainStatsCalculator
{
    List<MainChampionStat> Calculate(
        string platformId,
        string puuid,
        IReadOnlyCollection<ParticipantRow> participantRows,
        MainAnalysisOptions options,
        ChampionCoverageSnapshot coverage,
        DateTime calculatedAtUtc);
}
