using Core.Options;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class ChampionPatternAggregationProcess(
    ILogger<ChampionPatternAggregationProcess> logger,
    IProcessRunRecorder runRecorder,
    IOptions<MainAnalysisOptions> analysisOptions,
    ChampionPatternSourceRowReader sourceRowReader,
    ChampionPatternAggregateBuilder aggregateBuilder,
    ChampionPatternAggregatePersister aggregatePersister)
{
    private const string ProcessName = "ChampionPatternAggregation";

    public async Task RunAsync(CancellationToken ct)
    {
        var startedAtUtc = DateTime.UtcNow;

        try
        {
            var queueId = analysisOptions.Value.QueueId;
            var aggregationInputs = await sourceRowReader.LoadAggregationInputsAsync(queueId, ct);
            if (aggregationInputs.SourceRows.Count == 0 && aggregationInputs.ExistingAggregateScopes.Count == 0)
            {
                logger.LogInformation("No specialist-backed source rows available for champion pattern aggregation.");
                await runRecorder.RecordNoOpAsync(
                    ProcessName,
                    startedAtUtc,
                    new { reason = "No specialist-backed source rows available for champion pattern aggregation.", aggregateRows = 0 },
                    ct);
                return;
            }

            var aggregationResult = await aggregateBuilder.BuildAggregatesAsync(
                aggregationInputs.SourceRows,
                DateTime.UtcNow,
                ct);
            await aggregatePersister.ReplaceAggregatesAsync(
                aggregationInputs.ExistingAggregateScopes,
                aggregationResult.AggregateRows,
                ct);

            logger.LogInformation(
                "Champion pattern aggregation summary: sourceRows={SourceRows}, aggregateRows={AggregateRows}.",
                aggregationResult.SourceRowCount,
                aggregationResult.AggregateRows.Count);

            await runRecorder.RecordSuccessAsync(
                ProcessName,
                startedAtUtc,
                BuildSuccessPayload(aggregationResult),
                ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordFailureAsync(ProcessName, startedAtUtc, ex, ct);
            throw;
        }
    }

    private static object BuildSuccessPayload(ChampionPatternAggregationResult aggregationResult)
    {
        return new
        {
            sourceRows = aggregationResult.SourceRowCount,
            aggregateRows = aggregationResult.AggregateRows.Count,
            gameVersions = aggregationResult.AggregateRows.Select(a => a.GameVersion).Distinct().Count(),
            champions = aggregationResult.AggregateRows.Select(a => a.ChampionId).Distinct().Count()
        };
    }
}
