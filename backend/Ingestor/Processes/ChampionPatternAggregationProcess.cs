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

        // Chunk the aggregation one champion at a time. Loading every live-patch
        // match_participant (with its full item/skill timeline) into memory at
        // once grew the managed heap to ~6 GB and got the process OOM-killed,
        // taking the whole VPS down with it. Per-champion the working set is
        // bounded by a single champion's rows, and each champion's replace-by-
        // scope persist commits independently — a mid-run crash leaves already-
        // processed champions on fresh data and the rest on their previous data,
        // every scope internally consistent. The next run finishes the rest.
        var livePatchKeys = await sourceRowReader.LoadLivePatchKeysAsync(queueId, ct);
        var championIds = await sourceRowReader.LoadChampionIdsAsync(queueId, ct);

        if (championIds.Count == 0)
        {
            logger.LogInformation("No champions available for champion pattern aggregation.");
            return new { reason = "No champions available for champion pattern aggregation.", patterns = 0 };
        }

        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var totalSourceRows = 0;
        var totalScopes = 0;
        var totalPatterns = 0;
        var processedChampions = 0;
        var gameVersions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var championId in championIds)
        {
            ct.ThrowIfCancellationRequested();

            var inputs = await sourceRowReader.LoadAggregationInputsAsync(queueId, championId, livePatchKeys, ct);
            if (inputs.SourceRows.Count == 0 && inputs.ExistingAggregateScopes.Count == 0)
            {
                continue;
            }

            var result = await aggregateBuilder.BuildAggregatesAsync(inputs.SourceRows, aggregatedAtUtc, ct);
            await aggregatePersister.ReplaceAggregatesAsync(
                inputs.ExistingAggregateScopes,
                result.Scopes,
                result.Patterns,
                ct);

            totalSourceRows += result.SourceRowCount;
            totalScopes += result.Scopes.Count;
            totalPatterns += result.Patterns.Count;
            processedChampions++;
            foreach (var scope in result.Scopes)
            {
                gameVersions.Add(scope.GameVersion);
            }

            logger.LogDebug(
                "Champion {ChampionId} pattern aggregation: sourceRows={SourceRows}, scopes={ScopeCount}, patterns={PatternCount}.",
                championId,
                result.SourceRowCount,
                result.Scopes.Count,
                result.Patterns.Count);
        }

        logger.LogInformation(
            "Champion pattern aggregation summary: champions={Champions}, sourceRows={SourceRows}, scopes={ScopeCount}, patterns={PatternCount}.",
            processedChampions,
            totalSourceRows,
            totalScopes,
            totalPatterns);

        return new
        {
            sourceRows = totalSourceRows,
            scopes = totalScopes,
            patterns = totalPatterns,
            gameVersions = gameVersions.Count,
            champions = processedChampions
        };
    }
}
