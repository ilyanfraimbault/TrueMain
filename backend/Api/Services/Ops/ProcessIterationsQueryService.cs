using Data.Entities;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class ProcessIterationsQueryService(IProcessRunStore store) : IProcessIterationsQueryService
{
    private const int DefaultPageSize = 10;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 50;

    public async Task<ProcessIterationsReadModel> GetAsync(int? page, int? pageSize, bool finishedOnly, CancellationToken ct)
    {
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        // Capture "now" once, up front, so the finishedOnly store filter and the
        // in-memory effective-status mapping below judge staleness against the exact
        // same instant — capturing it twice could disagree by the query's duration.
        var now = DateTime.UtcNow;
        var freshCutoff = now - ProcessRunStaleness.Threshold;

        // One header per iteration, newest-first by the pass start, paged inside
        // the store before any per-run materialisation so a deep history stays
        // cheap. `finishedOnly` drops the in-flight pass (a Running run with a
        // still-fresh heartbeat — the same staleness rule the read mapping uses)
        // from BOTH the page and the total.
        var headerPage = await store.QueryIterationsAsync(
            effectivePage, effectivePageSize, finishedOnly, freshCutoff, ct);

        if (headerPage.Headers.Count == 0)
        {
            return new ProcessIterationsReadModel
            {
                Iterations = [],
                Total = headerPage.Total,
                Page = effectivePage,
                PageSize = effectivePageSize
            };
        }

        // Pull every run for the page's iterations in one query, then group in
        // memory. The page is capped at MaxPageSize iterations, so this is a small,
        // bounded fetch.
        var runDocuments = await store.GetRunsForIterationsAsync(
            headerPage.Headers.Select(header => header.IterationId).ToList(), ct);

        var runsByIteration = runDocuments
            .GroupBy(run => run.IterationId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        // Age stale-heartbeat Running rows out to Abandoned in memory (the store
        // returns raw statuses), so IsRunning is true only when a run is
        // *genuinely* in flight. Reuses the single `now` captured above so the
        // page and the finishedOnly filter agree.
        var iterations = headerPage.Headers
            .Select(header =>
            {
                var runs = runsByIteration.TryGetValue(header.IterationId, out var list) ? list : [];
                return new ProcessIterationReadModel
                {
                    IterationId = header.IterationId,
                    StartedAtUtc = header.StartedAtUtc,
                    LastActivityAtUtc = runs.Count == 0
                        ? header.StartedAtUtc
                        : runs.Max(run => run.FinishedAtUtc),
                    // Every run of a pass carries the same mode, so the first non-null one
                    // answers for the iteration; null only when the whole pass predates
                    // #1362's stamping.
                    JobMode = runs.Select(run => run.JobMode).FirstOrDefault(mode => mode is not null),
                    IsRunning = runs.Any(run =>
                        ProcessRunStaleness.EffectiveStatus(run.Status, run.LastHeartbeatAtUtc, now)
                        == ProcessRunStatus.Running),
                    Runs = runs
                        .Select(run => ProcessRunsQueryService.ToReadModel(run, now))
                        .ToList()
                };
            })
            .ToList();

        return new ProcessIterationsReadModel
        {
            Iterations = iterations,
            Total = headerPage.Total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }
}
