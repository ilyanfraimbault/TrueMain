using Data.Entities;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class ProcessRunsQueryService(IProcessRunStore store) : IProcessRunsQueryService
{
    private const int DefaultPageSize = 100;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 500;

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
        // The runs list and the rollup's failure window share the SAME `since`.
        //
        // Runs: return the requested page of runs ordered newest-first with NO
        // default time lower bound, so the admin panel can always show the last N
        // runs even when nothing ran recently. A `since` lower bound is applied to
        // the runs list ONLY when the caller explicitly provides it.
        //
        // Failure window: the rollup's in-window counts follow the same `since`,
        // with NO hidden default. When `since` is omitted the window is unbounded,
        // so FailureCountInWindow is a true all-time total (≥ any narrower window)
        // rather than a secret 7-day count that could be smaller than a wider
        // explicit window. When `since` is provided it narrows the runs list and
        // the in-window counts consistently.
        var windowStart = since;
        var normalizedProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        var statusFilter = ParseStatus(status);

        var pageResult = await store.QueryRunsAsync(
            normalizedProcessName, statusFilter, since, effectivePage, effectivePageSize, ct);

        // Age stale-heartbeat Running rows out to Abandoned in memory (the store
        // returns raw statuses), so a run whose host died surfaces as Abandoned
        // rather than perpetually Running.
        var now = DateTime.UtcNow;

        var runs = pageResult.Runs
            .Select(run => ToReadModel(run, now))
            .ToList();

        var rollupRows = await store.GetRollupsAsync(normalizedProcessName, windowStart, ct);

        var rollup = rollupRows
            .Select(row => new ProcessRunRollupReadModel
            {
                ProcessName = row.ProcessName,
                // Map the latest run's raw status through the staleness policy so a
                // stale Running latest-run shows as Abandoned, consistent with the
                // runs list.
                LastStatus = ProcessRunStaleness
                    .EffectiveStatus(row.LatestStatus, row.LatestHeartbeatAtUtc, now)
                    .ToString(),
                LastRunAtUtc = row.LastRunAtUtc,
                LastSuccessAtUtc = row.LastSuccessAtUtc,
                FailureCountInWindow = (int)row.FailureCountInWindow,
                RunCountInWindow = (int)row.RunCountInWindow,
                FailureRateInWindow = row.RunCountInWindow == 0
                    ? 0d
                    : (double)row.FailureCountInWindow / row.RunCountInWindow
            })
            .ToList();

        return new ProcessRunsReadModel
        {
            Runs = runs,
            Rollup = rollup,
            Total = pageResult.Total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }

    internal static ProcessRunReadModel ToReadModel(ProcessRunDocument run, DateTime now)
        => new()
        {
            Id = run.Id,
            ProcessName = run.ProcessName,
            StartedAtUtc = run.StartedAtUtc,
            FinishedAtUtc = run.FinishedAtUtc,
            DurationMs = run.DurationMs,
            Status = ProcessRunStaleness.EffectiveStatus(run.Status, run.LastHeartbeatAtUtc, now).ToString(),
            Error = run.Error,
            Host = run.Host,
            JobMode = run.JobMode,
            LastHeartbeatAtUtc = run.LastHeartbeatAtUtc,
            Summary = ProcessRunSummaryParsing.Parse(run.SummaryJson)
        };

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
