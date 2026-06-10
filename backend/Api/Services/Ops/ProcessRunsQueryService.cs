using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class ProcessRunsQueryService(TrueMainDbContext db) : IProcessRunsQueryService
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const int FailureWindowDays = 7;

    public async Task<ProcessRunsReadModel> GetAsync(
        string? processName,
        string? status,
        DateTime? since,
        int? limit,
        CancellationToken ct)
    {
        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        // The runs list and the rollup's failure window are independent.
        //
        // Runs: return the most recent `limit` runs ordered newest-first with NO
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

        var runEntities = await runsQuery
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .Take(effectiveLimit)
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
            Rollup = rollup
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
            .Select(run => new { run.ProcessName, run.StartedAtUtc, run.Status })
            .ToListAsync(ct);

        var latestStatusByProcess = latestStatusRows
            .GroupBy(row => row.ProcessName)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.StartedAtUtc)
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
