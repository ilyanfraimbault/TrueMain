using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class ProcessIterationsQueryService(TrueMainDbContext db) : IProcessIterationsQueryService
{
    private const int DefaultPageSize = 10;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 50;

    // A Running row whose heartbeat (refreshed every 30s) is older than this — or
    // missing entirely — is treated as Abandoned: its owner died without
    // finalising it. Four missed beats keeps a healthy-but-slow run from flapping.
    private static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(2);

    public async Task<ProcessIterationsReadModel> GetAsync(int? page, int? pageSize, CancellationToken ct)
    {
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        // Only iteration-stamped runs group into the chain view; historical
        // un-grouped rows (IterationId == null) stay in the flat runs feed.
        var grouped = db.ProcessRuns
            .AsNoTracking()
            .Where(run => run.IterationId != null);

        // One header row per iteration, ordered newest-first by when the pass
        // began, paged before any per-run materialisation so a deep history stays
        // cheap. Identify each iteration by its id plus the start/last-activity it
        // already exposes, then fetch the runs for just this page below.
        var iterationKeysQuery = grouped
            .GroupBy(run => run.IterationId!.Value)
            .Select(group => new
            {
                IterationId = group.Key,
                StartedAtUtc = group.Min(run => run.StartedAtUtc)
            })
            .OrderByDescending(key => key.StartedAtUtc)
            .ThenByDescending(key => key.IterationId);

        var total = await iterationKeysQuery.LongCountAsync(ct);

        var pageKeys = await iterationKeysQuery
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(ct);

        if (pageKeys.Count == 0)
        {
            return new ProcessIterationsReadModel
            {
                Iterations = [],
                Total = total,
                Page = effectivePage,
                PageSize = effectivePageSize
            };
        }

        var pageIterationIds = pageKeys.Select(key => key.IterationId).ToList();

        // Pull every run for the page's iterations in one query, then group in
        // memory. The page is capped at MaxPageSize iterations, so this is a small,
        // bounded fetch.
        var runEntities = await db.ProcessRuns
            .AsNoTracking()
            .Where(run => run.IterationId != null && pageIterationIds.Contains(run.IterationId.Value))
            .OrderBy(run => run.StartedAtUtc)
            .ThenBy(run => run.Id)
            .ToListAsync(ct);

        var runsByIteration = runEntities
            .GroupBy(run => run.IterationId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        // Age stale-heartbeat Running rows out to Abandoned in memory (the
        // freshness math can't be expressed in the SQL projection cheaply), so
        // IsRunning is true only when a run is *genuinely* in flight.
        var now = DateTime.UtcNow;

        var iterations = pageKeys
            .Select(key =>
            {
                var runs = runsByIteration.TryGetValue(key.IterationId, out var list) ? list : [];
                return new ProcessIterationReadModel
                {
                    IterationId = key.IterationId,
                    StartedAtUtc = key.StartedAtUtc,
                    LastActivityAtUtc = runs.Count == 0
                        ? key.StartedAtUtc
                        : runs.Max(run => run.FinishedAtUtc),
                    IsRunning = runs.Any(run => EffectiveStatus(run, now) == ProcessRunStatus.Running),
                    Runs = runs
                        .Select(run => new ProcessRunReadModel
                        {
                            Id = run.Id,
                            ProcessName = run.ProcessName,
                            StartedAtUtc = run.StartedAtUtc,
                            FinishedAtUtc = run.FinishedAtUtc,
                            DurationMs = run.DurationMs,
                            Status = EffectiveStatus(run, now).ToString(),
                            Error = run.Error,
                            Host = run.Host,
                            LastHeartbeatAtUtc = run.LastHeartbeatAtUtc,
                            // Clone so the value outlives the JsonDocument; written as raw JSON.
                            Summary = run.Summary?.RootElement.Clone()
                        })
                        .ToList()
                };
            })
            .ToList();

        return new ProcessIterationsReadModel
        {
            Iterations = iterations,
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }

    // A Running row reads as Abandoned once its heartbeat is missing or older than
    // the stale threshold; every other status passes through unchanged. The
    // single-instance ingestor abandons orphans at startup too, but this covers a
    // run whose host is still up but died/hung between restarts.
    private static ProcessRunStatus EffectiveStatus(ProcessRun run, DateTime now)
    {
        if (run.Status != ProcessRunStatus.Running)
        {
            return run.Status;
        }

        var stale = run.LastHeartbeatAtUtc is null
            || run.LastHeartbeatAtUtc < now - StaleRunningThreshold;
        return stale ? ProcessRunStatus.Abandoned : ProcessRunStatus.Running;
    }
}
