using Core.Options;
using Ingestor.Processes.Components.PatternAggregation;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class ChampionPatternAggregationProcess(
    ILogger<ChampionPatternAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    ChampionPatternSourceRowReader sourceRowReader,
    ChampionPatternAggregateBuilder aggregateBuilder,
    ChampionPatternAggregatePersister aggregatePersister,
    TimeProvider timeProvider) : IIngestorProcess
{
    public string Name => "ChampionPatternAggregation";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var aggregationInputs = await sourceRowReader.LoadAggregationInputsAsync(queueId, ct);
        if (aggregationInputs.SourceRows.Count == 0 && aggregationInputs.ExistingAggregateScopes.Count == 0)
        {
            logger.LogInformation("No specialist-backed source rows available for champion pattern aggregation.");
            return new { reason = "No specialist-backed source rows available for champion pattern aggregation.", patterns = 0 };
        }

        var aggregationResult = await aggregateBuilder.BuildAggregatesAsync(
            aggregationInputs.SourceRows,
            timeProvider.GetUtcNow().UtcDateTime,
            ct);
        await aggregatePersister.ReplaceAggregatesAsync(
            aggregationInputs.ExistingAggregateScopes,
            aggregationResult.Scopes,
            aggregationResult.Patterns,
            ct);

        logger.LogInformation(
            "Champion pattern aggregation summary: sourceRows={SourceRows}, scopes={ScopeCount}, patterns={PatternCount}.",
            aggregationResult.SourceRowCount,
            aggregationResult.Scopes.Count,
            aggregationResult.Patterns.Count);

        return BuildSuccessPayload(aggregationResult);
    }

    private static object BuildSuccessPayload(ChampionPatternAggregationResult aggregationResult)
    {
        return new
        {
            sourceRows = aggregationResult.SourceRowCount,
            scopes = aggregationResult.Scopes.Count,
            patterns = aggregationResult.Patterns.Count,
            gameVersions = aggregationResult.Scopes.Select(scope => scope.GameVersion).Distinct().Count(),
            champions = aggregationResult.Scopes.Select(scope => scope.ChampionId).Distinct().Count()
        };
    }
}
