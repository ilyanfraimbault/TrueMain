using Core.Options;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Processes.Components.MainAnalysis;

public interface IMainStatsCalculator
{
    List<MainChampionStat> Calculate(
        string platformId,
        string puuid,
        IReadOnlyCollection<ParticipantRow> participantRows,
        MainAnalysisOptions options,
        DateTime calculatedAtUtc);
}
