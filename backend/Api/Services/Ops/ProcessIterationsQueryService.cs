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

    public async Task<ProcessIterationsReadModel> GetAsync(int? page, int? pageSize, bool finishedOnly, CancellationToken ct)
    {
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        // Single "now" for the whole request so the finishedOnly SQL filter and the
        // in-memory effective-status mapping below use the exact same instant (the
        // same convention as ProcessRunsQueryService).
        var now = DateTime.UtcNow;

        // Only iteration-stamped runs group into the chain view; historical
        // un-grouped rows (IterationId == null) stay in the flat runs feed.
        var grouped = db.ProcessRuns
            .AsNoTracking()
            .Where(run => run.IterationId != null);

        // `finishedOnly` drops the in-flight pass from BOTH the page and the total,
        // so the completed-history list paginates without an off-by-one (the chain
        // view fetches the running iteration separately). An iteration is in flight
        // when it has a Running run with a still-fresh heartbeat — the same
        // staleness rule the read mapping uses, expressed here as a NOT EXISTS so
        // the count stays correct pre-paging.
        if (finishedOnly)
        {
            var freshCutoff = now - ProcessRunStaleness.Threshold;
            grouped = grouped.Where(run => !db.ProcessRuns.Any(other =>
                other.IterationId == run.IterationId
                && other.Status == ProcessRunStatus.Running
                && other.LastHeartbeatAtUtc != null
                && other.LastHeartbeatAtUtc >= freshCutoff));
        }

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
        // IsRunning is true only when a run is *genuinely* in flight. Reuses the
        // single `now` captured above so the page and the finishedOnly filter agree.
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
                    IsRunning = runs.Any(run => ProcessRunStaleness.EffectiveStatus(run.Status, run.LastHeartbeatAtUtc, now) == ProcessRunStatus.Running),
                    Runs = runs
                        .Select(run => new ProcessRunReadModel
                        {
                            Id = run.Id,
                            ProcessName = run.ProcessName,
                            StartedAtUtc = run.StartedAtUtc,
                            FinishedAtUtc = run.FinishedAtUtc,
                            DurationMs = run.DurationMs,
                            Status = ProcessRunStaleness.EffectiveStatus(run.Status, run.LastHeartbeatAtUtc, now).ToString(),
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

}
