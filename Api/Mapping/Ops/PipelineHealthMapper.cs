using TrueMain.Contracts.Ops;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Mapping.Ops;

public static class PipelineHealthMapper
{
    public static PipelineHealthResponse ToContract(this PipelineHealthReadModel readModel)
    {
        return new PipelineHealthResponse
        {
            Processes = readModel.Processes
                .Select(process => new ProcessHealthResponse
                {
                    ProcessName = process.ProcessName,
                    Status = process.Status,
                    LastStartedAtUtc = process.LastStartedAtUtc,
                    LastFinishedAtUtc = process.LastFinishedAtUtc,
                    DurationMs = process.DurationMs,
                    Error = process.Error
                })
                .ToList(),
            RawData = new RawDataFreshnessResponse
            {
                QueueId = readModel.RawData.QueueId,
                RawMatchCount = readModel.RawData.RawMatchCount,
                RawParticipantCount = readModel.RawData.RawParticipantCount,
                Platforms = readModel.RawData.Platforms
                    .Select(platform => new PlatformRawDataFreshnessResponse
                    {
                        PlatformId = platform.PlatformId,
                        LatestMatchStartAtUtc = platform.LatestMatchStartAtUtc,
                        LatestPatchVersion = platform.LatestPatchVersion
                    })
                    .ToList()
            },
            Gaps = new PipelineGapResponse
            {
                MatchIngestionToMainAnalysisMinutes = readModel.Gaps.MatchIngestionToMainAnalysisMinutes,
                ChampionDataLagMinutes = readModel.Gaps.ChampionDataLagMinutes
            }
        };
    }
}
