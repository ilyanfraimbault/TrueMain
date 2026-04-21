using Core.Options;
using Ingestor.Processes.Components.PatternAggregation;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class ChampionPatternAggregationProcess(
    ILogger<ChampionPatternAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    ChampionPatternSourceRowReader sourceRowReader,
    ChampionPatternAggregateBuilder aggregateBuilder,
    ChampionPatternAggregatePersister aggregatePersister) : IIngestorProcess
{
    public string Name => "ChampionPatternAggregation";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = analysisOptions.Value.QueueId;
        var aggregationInputs = await sourceRowReader.LoadAggregationInputsAsync(queueId, ct);
        if (aggregationInputs.SourceRows.Count == 0 && aggregationInputs.ExistingAggregateScopes.Count == 0)
        {
            logger.LogInformation("No specialist-backed source rows available for champion pattern aggregation.");
            return new { reason = "No specialist-backed source rows available for champion pattern aggregation.", aggregateRows = 0 };
        }

        var aggregationResult = await aggregateBuilder.BuildAggregatesAsync(
            aggregationInputs.SourceRows,
            DateTime.UtcNow,
            ct);
        await aggregatePersister.ReplaceAggregatesAsync(
            aggregationInputs.ExistingAggregateScopes,
            aggregationResult.AggregateRows,
            aggregationResult.Scopes,
            ct);

        logger.LogInformation(
            "Champion pattern aggregation summary: sourceRows={SourceRows}, aggregateRows={AggregateRows}, scopes={ScopeCount}.",
            aggregationResult.SourceRowCount,
            aggregationResult.AggregateRows.Count,
            aggregationResult.Scopes.Count);

        return BuildSuccessPayload(aggregationResult);
    }

    private static object BuildSuccessPayload(ChampionPatternAggregationResult aggregationResult)
    {
        return new
        {
            sourceRows = aggregationResult.SourceRowCount,
            aggregateRows = aggregationResult.AggregateRows.Count,
            scopes = aggregationResult.Scopes.Count,
            gameVersions = aggregationResult.AggregateRows.Select(a => a.GameVersion).Distinct().Count(),
            champions = aggregationResult.AggregateRows.Select(a => a.ChampionId).Distinct().Count()
        };
    }
}
