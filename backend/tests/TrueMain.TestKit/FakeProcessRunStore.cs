using Data.Entities;
using Data.Ops.Mongo;

namespace TrueMain.TestKit;

/// <summary>
/// In-memory <see cref="IProcessRunStore"/> for tests whose subject only touches
/// process runs incidentally (e.g. Discovery's cadence gate). Writes are captured
/// in <see cref="Runs"/>; the cadence read serves
/// <see cref="LastCompletedRunStartUtc"/> verbatim; the admin query methods
/// return empty results.
/// </summary>
public sealed class FakeProcessRunStore : IProcessRunStore
{
    public List<ProcessRunDocument> Runs { get; } = [];

    /// <summary>What <see cref="GetLastCompletedRunStartAsync"/> returns.</summary>
    public DateTime? LastCompletedRunStartUtc { get; set; }

    public Task InsertAsync(ProcessRunDocument run, CancellationToken ct)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<bool> FinalizeAsync(
        Guid id,
        DateTime finishedAtUtc,
        int durationMs,
        ProcessRunStatus status,
        string? error,
        string? summaryJson,
        CancellationToken ct)
    {
        var run = Runs.FirstOrDefault(candidate => candidate.Id == id);
        if (run is null)
        {
            return Task.FromResult(false);
        }

        run.FinishedAtUtc = finishedAtUtc;
        run.DurationMs = durationMs;
        run.Status = status;
        run.Error = error;
        run.SummaryJson = summaryJson;
        return Task.FromResult(true);
    }

    public Task TouchHeartbeatAsync(Guid id, DateTime nowUtc, CancellationToken ct)
    {
        var run = Runs.FirstOrDefault(candidate => candidate.Id == id && candidate.Status == ProcessRunStatus.Running);
        run?.LastHeartbeatAtUtc = nowUtc;

        return Task.CompletedTask;
    }

    public Task<int> AbandonRunningAsync(DateTime finishedAtUtc, string error, IReadOnlyCollection<string> processNames, CancellationToken ct)
    {
        var running = Runs.Where(run => run.Status == ProcessRunStatus.Running).ToList();
        foreach (var run in running)
        {
            run.Status = ProcessRunStatus.Abandoned;
            run.FinishedAtUtc = finishedAtUtc;
            run.Error = error;
        }

        return Task.FromResult(running.Count);
    }

    public Task<DateTime?> GetLastCompletedRunStartAsync(string processName, CancellationToken ct)
        => Task.FromResult(LastCompletedRunStartUtc);

    public Task<ProcessRunPage> QueryRunsAsync(
        string? processName,
        ProcessRunStatus? status,
        DateTime? since,
        int page,
        int pageSize,
        CancellationToken ct)
        => Task.FromResult(new ProcessRunPage([], 0));

    /// <summary>
    /// Serves this one from <see cref="Runs"/> rather than returning empty like the
    /// other stubs: it is what the ingestion-throughput and candidate-funnel series
    /// read, so a test that seeds runs here must see them.
    /// </summary>
    public Task<IReadOnlyList<ProcessRunSummarySample>> GetRunSummariesAsync(
        IReadOnlyCollection<string> processNames,
        DateTime sinceUtc,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProcessRunSummarySample>>(
            [.. Runs
                .Where(run => processNames.Contains(run.ProcessName) && run.StartedAtUtc >= sinceUtc)
                .OrderBy(run => run.StartedAtUtc)
                .Select(run => new ProcessRunSummarySample(run.ProcessName, run.StartedAtUtc, run.SummaryJson))]);

    public Task<IReadOnlyList<ProcessRunRollup>> GetRollupsAsync(
        string? processName,
        DateTime? windowStart,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProcessRunRollup>>([]);

    public Task<ProcessIterationHeaderPage> QueryIterationsAsync(
        int page,
        int pageSize,
        bool finishedOnly,
        DateTime freshHeartbeatCutoff,
        CancellationToken ct)
        => Task.FromResult(new ProcessIterationHeaderPage([], 0));

    public Task<IReadOnlyList<ProcessRunDocument>> GetRunsForIterationsAsync(
        IReadOnlyCollection<Guid> iterationIds,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProcessRunDocument>>([]);

    public Task<IReadOnlyList<ProcessRunDocument>> GetLatestPerProcessAsync(
        IReadOnlyCollection<string> processNames,
        bool onlySuccesses,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProcessRunDocument>>([]);

    public Task<long> CountTerminalRunsSinceAsync(string processName, DateTime? afterUtc, CancellationToken ct)
        => Task.FromResult((long)Runs.Count(run =>
            run.ProcessName == processName
            && run.Status != ProcessRunStatus.Running
            && (afterUtc is null || run.StartedAtUtc > afterUtc.Value)));
}
