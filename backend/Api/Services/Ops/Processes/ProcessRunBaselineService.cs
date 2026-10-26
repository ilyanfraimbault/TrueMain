using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Processes;

/// <summary>
/// Attaches a baseline to each successful run of a page (#2298): one bounded store
/// lookup per distinct (process, mode) on the page — never one per row — each kept
/// for <see cref="ProcessRunBaselinePolicy.CacheTtl"/> since the history moves with
/// every new run.
/// </summary>
public interface IProcessRunBaselineService
{
    /// <summary>The read models of <paramref name="runs"/>, in order, with their baselines.</summary>
    Task<IReadOnlyList<ProcessRunReadModel>> AttachAsync(
        IReadOnlyList<ProcessRunDocument> runs, DateTime now, CancellationToken ct);
}

public sealed class ProcessRunBaselineService(IProcessRunStore store, IMemoryCache cache)
    : IProcessRunBaselineService
{
    public async Task<IReadOnlyList<ProcessRunReadModel>> AttachAsync(
        IReadOnlyList<ProcessRunDocument> runs, DateTime now, CancellationToken ct)
    {
        // Only a success is compared: a failed or skipped run's duration and counters
        // stop short by definition and would always read as "far below the median".
        var keys = runs
            .Where(run => run.Status == ProcessRunStatus.Success)
            .Select(run => new BaselineKey(run.ProcessName, run.JobMode))
            .Distinct()
            .ToList();

        var stats = await Task.WhenAll(keys.Select(key => GetStatsAsync(key, ct)));
        var byKey = keys.Zip(stats).ToDictionary(pair => pair.First, pair => pair.Second);

        return runs
            .Select(run =>
            {
                var model = ProcessRunsQueryService.ToReadModel(run, now);
                return run.Status == ProcessRunStatus.Success
                       && byKey.TryGetValue(new BaselineKey(run.ProcessName, run.JobMode), out var history)
                    ? model with { Baseline = ProcessRunBaselineMath.Measure(run.DurationMs, run.SummaryJson, history) }
                    : model;
            })
            .ToList();
    }

    private async Task<ProcessRunBaselineStats> GetStatsAsync(BaselineKey key, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out ProcessRunBaselineStats? cached) && cached is not null)
        {
            return cached;
        }

        var samples = await store.GetRecentSuccessesAsync(
            key.ProcessName, key.JobMode, ProcessRunBaselinePolicy.SampleSize, ct);

        return cache.Store(key, ProcessRunBaselineMath.Summarise(samples), ProcessRunBaselinePolicy.CacheTtl);
    }

    private sealed record BaselineKey(string ProcessName, string? JobMode);
}
