using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class ProcessRunsQueryService(TrueMainDbContext db) : IProcessRunsQueryService
{
    private const int DefaultPageSize = 100;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 500;
    private const int FailureWindowDays = 7;

    public async Task<ProcessRunsReadModel> GetAsync(
        string? processName,
        string? status,
        DateTime? since,
        int? limit,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        // Paging mirrors /ops/logs: 1-based `page` (clamped to >= 1) and
        // `pageSize` (clamped to [1, 500], default 100). The legacy `limit` param
        // predates paging and meant "the N most recent runs"; that is exactly
        // page 1 with pageSize=N, so it is honoured as the page size when
        // `pageSize` is absent and superseded by `pageSize` when both are sent.
        // The upper bound keeps `(page - 1) * pageSize` within int range even at
        // the maximum page size; pages that deep are far beyond any real data and
        // simply return an empty slice.
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? limit ?? DefaultPageSize, MinPageSize, MaxPageSize);
        // The runs list and the rollup's failure window are independent.
        //
        // Runs: return the requested page of runs ordered newest-first with NO
        // default time lower bound, so the admin panel can always show the last N
        // runs even when nothing ran recently. A `since` lower bound is applied to
        // the runs list ONLY when the caller explicitly provides it.
        //
        // Failure window: keep a bounded window for the rollup's
        // FailureCountInWindow with its own default (the last 7 days), independent
        // of the now-optional runs `since`. When `since` is explicitly provided we
        // honour it for the failure window too, so an explicit bound narrows both
        // the runs list and the failure count consistently.
        var failureWindowStart = since ?? DateTime.UtcNow.AddDays(-FailureWindowDays);
        var normalizedProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        var statusFilter = ParseStatus(status);

        var runsQuery = db.ProcessRuns.AsNoTracking();

        if (since is not null)
        {
            runsQuery = runsQuery.Where(run => run.StartedAtUtc >= since.Value);
        }

        if (normalizedProcessName is not null)
        {
            runsQuery = runsQuery.Where(run => run.ProcessName == normalizedProcessName);
        }

        if (statusFilter is not null)
        {
            runsQuery = runsQuery.Where(run => run.Status == statusFilter);
        }

        // Count before paging so the panel can render a pager; the rollup below
        // is likewise computed over the full filtered set, not the page.
        var total = await runsQuery.LongCountAsync(ct);

        var runEntities = await runsQuery
            // Newest first; Id breaks ties so paging is stable when several runs
            // share a StartedAtUtc.
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(ct);

        var runs = runEntities
            .Select(run => new ProcessRunReadModel
            {
                Id = run.Id,
                ProcessName = run.ProcessName,
                StartedAtUtc = run.StartedAtUtc,
                FinishedAtUtc = run.FinishedAtUtc,
                DurationMs = run.DurationMs,
                Status = run.Status.ToString(),
                Error = run.Error,
                Host = run.Host,
                // Clone the root element so the value is detached from the
                // JsonDocument's lifetime; System.Text.Json then writes it as
                // raw JSON in the response.
                Summary = run.Summary?.RootElement.Clone()
            })
            .ToList();

        var rollup = await BuildRollupAsync(normalizedProcessName, failureWindowStart, ct);

        return new ProcessRunsReadModel
        {
            Runs = runs,
            Rollup = rollup,
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }

    private async Task<IReadOnlyList<ProcessRunRollupReadModel>> BuildRollupAsync(
        string? processName,
        DateTime failureWindowStart,
        CancellationToken ct)
    {
        var rollupQuery = db.ProcessRuns.AsNoTracking();
        if (processName is not null)
        {
            rollupQuery = rollupQuery.Where(run => run.ProcessName == processName);
        }

        // One grouped pass per process. Latest status/run and last-success are
        // unbounded (so an idle process still reports its real last state), while
        // the failure count is scoped to the window. Computing Max over a boolean
        // projection of "started inside window AND failed" lets EF translate the
        // whole thing without a second round-trip.
        var groups = await rollupQuery
            .GroupBy(run => run.ProcessName)
            .Select(group => new
            {
                ProcessName = group.Key,
                LastRunAtUtc = group.Max(run => run.StartedAtUtc),
                LastSuccessAtUtc = group
                    .Where(run => run.Status == ProcessRunStatus.Success)
                    .Max(run => (DateTime?)run.FinishedAtUtc),
                FailureCountInWindow = group
                    .Count(run => run.StartedAtUtc >= failureWindowStart && run.Status == ProcessRunStatus.Failed)
            })
            .ToListAsync(ct);

        if (groups.Count == 0)
        {
            return [];
        }

        // Resolve the status of each process's latest run. The grouped query
        // above can't also carry the latest row's status without a correlated
        // subquery per group, so fetch the (processName, lastRunAtUtc) statuses
        // in one extra query keyed by the maxima we already have.
        var processNames = groups.Select(group => group.ProcessName).ToList();
        var lastRunStarts = groups.Select(group => group.LastRunAtUtc).ToList();

        var latestStatusRows = await db.ProcessRuns
            .AsNoTracking()
            .Where(run => processNames.Contains(run.ProcessName) && lastRunStarts.Contains(run.StartedAtUtc))
            .Select(run => new { run.ProcessName, run.StartedAtUtc, run.Status, run.Id })
            .ToListAsync(ct);

        // The query above filters on the process-name set AND the max-timestamp
        // set independently, so it can over-fetch when two processes happen to
        // share an identical max StartedAtUtc (it would return each one's run at
        // that timestamp for the other too). Pair (ProcessName, LastRunAtUtc)
        // explicitly here so the latest status is matched to the right process
        // rather than relying on the OrderByDescending below to compensate.
        var maxStartByProcess = groups.ToDictionary(group => group.ProcessName, group => group.LastRunAtUtc);

        var latestStatusByProcess = latestStatusRows
            .Where(row => maxStartByProcess.TryGetValue(row.ProcessName, out var max) && row.StartedAtUtc == max)
            .GroupBy(row => row.ProcessName)
            .ToDictionary(
                group => group.Key,
                // Ties at the same timestamp are still possible (a process logging
                // two runs with identical StartedAtUtc); Id breaks the tie so the
                // pick is deterministic rather than dependent on row order.
                group => group
                    .OrderByDescending(row => row.StartedAtUtc)
                    .ThenByDescending(row => row.Id)
                    .First()
                    .Status);

        return groups
            .OrderBy(group => group.ProcessName)
            .Select(group => new ProcessRunRollupReadModel
            {
                ProcessName = group.ProcessName,
                LastStatus = latestStatusByProcess.TryGetValue(group.ProcessName, out var lastStatus)
                    ? lastStatus.ToString()
                    : string.Empty,
                LastRunAtUtc = group.LastRunAtUtc,
                LastSuccessAtUtc = group.LastSuccessAtUtc,
                FailureCountInWindow = group.FailureCountInWindow
            })
            .ToList();
    }

    private static ProcessRunStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return Enum.TryParse<ProcessRunStatus>(status.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }
}
