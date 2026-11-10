using Data.Entities;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Processes;

public interface IProcessRunsQueryService
{
    Task<ProcessRunsReadModel> GetAsync(
        string? processName,
        string? status,
        DateTime? since,
        int? limit,
        int? page,
        int? pageSize,
        CancellationToken ct);

    /// <summary>
    /// Per-process health over the window that starts at <paramref name="since"/> (unbounded when
    /// null), with the verdict a panel colours by.
    /// </summary>
    Task<IReadOnlyList<ProcessRunRollupReadModel>> GetRollupAsync(string? processName, DateTime? since, CancellationToken ct);
}

public sealed class ProcessRunsQueryService(IProcessRunStore store, IProcessRunBaselineService baselines)
    : IProcessRunsQueryService
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
        // No default time lower bound: the panel always shows the last N runs, even when
        // nothing ran recently. `since` narrows the list only when the caller sends it.
        var normalizedProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        var statusFilter = ParseStatus(status);

        var pageResult = await store.QueryRunsAsync(
            normalizedProcessName, statusFilter, since, effectivePage, effectivePageSize, ct);

        // Age stale-heartbeat Running rows out to Abandoned in memory (the store
        // returns raw statuses), so a run whose host died surfaces as Abandoned
        // rather than perpetually Running.
        var now = DateTime.UtcNow;

        var runs = await baselines.AttachAsync(pageResult.Runs, now, ct);

        return new ProcessRunsReadModel
        {
            Runs = runs,
            Total = pageResult.Total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }

    /// <summary>
    /// The share of a window's runs that failed from which a process reads as degraded even
    /// though its latest run succeeded. A display cue for the health grid, not a published figure.
    /// </summary>
    public const double DegradedFailureRate = 0.25;

    public async Task<IReadOnlyList<ProcessRunRollupReadModel>> GetRollupAsync(
        string? processName, DateTime? since, CancellationToken ct)
    {
        // No hidden default window: without `since` the counts are true all-time totals,
        // never a secret 7-day count smaller than a wider explicit window.
        var normalizedProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        var now = DateTime.UtcNow;
        var rollupRows = await store.GetRollupsAsync(normalizedProcessName, since, now, ct);

        return
        [
            .. rollupRows.Select(row =>
            {
                // A stale Running latest run reads as Abandoned, consistent with the runs list.
                var lastStatus = ProcessRunStaleness.EffectiveStatus(row.LatestStatus, row.LatestHeartbeatAtUtc, now);
                var failureRate = row.RunCountInWindow == 0
                    ? 0d
                    : (double)row.FailureCountInWindow / row.RunCountInWindow;
                return new ProcessRunRollupReadModel
                {
                    ProcessName = row.ProcessName,
                    LastStatus = lastStatus,
                    LastRunAtUtc = row.LastRunAtUtc,
                    LastSuccessAtUtc = row.LastSuccessAtUtc,
                    FailureCountInWindow = (int)row.FailureCountInWindow,
                    RunCountInWindow = (int)row.RunCountInWindow,
                    FailureRateInWindow = failureRate,
                    Health = HealthOf(lastStatus, failureRate)
                };
            })
        ];
    }

    /// <summary>
    /// Red when the latest run failed, amber when it did not but the window's failure rate
    /// reaches <see cref="DegradedFailureRate"/>, green otherwise.
    /// </summary>
    internal static DetectorStatus HealthOf(ProcessRunStatus lastStatus, double failureRate)
        => (lastStatus, failureRate >= DegradedFailureRate) switch
        {
            (ProcessRunStatus.Failed, _) => DetectorStatus.Red,
            (_, true) => DetectorStatus.Amber,
            _ => DetectorStatus.Green,
        };

    internal static ProcessRunReadModel ToReadModel(ProcessRunDocument run, DateTime now)
    {
        var status = ProcessRunStaleness.EffectiveStatus(run.Status, run.LastHeartbeatAtUtc, now);
        return new()
        {
            Id = run.Id,
            ProcessName = run.ProcessName,
            StartedAtUtc = run.StartedAtUtc,
            // The store mirrors an unfinished run's start into its finish; that is not a finish.
            FinishedAtUtc = run.Status == ProcessRunStatus.Running ? null : run.FinishedAtUtc,
            DurationMs = run.DurationMs,
            Status = status,
            Error = run.Error,
            Host = run.Host,
            JobMode = run.JobMode,
            LastHeartbeatAtUtc = run.LastHeartbeatAtUtc,
            Summary = ProcessRunSummaryParsing.Parse(run.SummaryJson)
        };
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
