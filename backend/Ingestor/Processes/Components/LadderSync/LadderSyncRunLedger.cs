using System.Text.Json;
using Data.Ops.Mongo;
using Ingestor.Options;

namespace Ingestor.Processes.Components.LadderSync;

/// <summary>
/// What the ladder sync has already spent and refreshed recently, read back from its own run
/// summaries (#1474). The daily ceiling and the apex cadence both need to know about runs other
/// than the current one, and the summaries are the only record of them.
/// </summary>
internal sealed record LadderSyncRunLedger(int PagedCallsToday, DateTime? LastApexRunUtc)
{
    private static LadderSyncRunLedger Empty { get; } = new(0, null);

    /// <summary>
    /// Reads the ledger of <paramref name="processName"/> as of <paramref name="nowUtc"/>.
    /// </summary>
    /// <remarks>
    /// One indexed range scan serves both questions: the window starts at UTC midnight for the
    /// budget, and reaches further back when the apex interval is longer than the day so far.
    /// Runs whose summary is missing (failed before summarising, still running) count for
    /// nothing — a call that was never recorded cannot be charged.
    /// </remarks>
    public static async Task<LadderSyncRunLedger> ReadAsync(
        IProcessRunStore processRunStore,
        string processName,
        LadderSyncOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var needsBudget = options.MaxRequestsPerDay > 0;
        var needsApex = options.ApexRefreshInterval > TimeSpan.Zero;
        if (!needsBudget && !needsApex)
        {
            return Empty;
        }

        var midnightUtc = nowUtc.Date;
        var since = needsApex && nowUtc - options.ApexRefreshInterval < midnightUtc
            ? nowUtc - options.ApexRefreshInterval
            : midnightUtc;

        var pagedCallsToday = 0;
        DateTime? lastApexRunUtc = null;

        foreach (var run in await processRunStore.GetRunSummariesAsync([processName], since, ct))
        {
            if (run.SummaryJson is null)
            {
                continue;
            }

            var (pagedCalls, apexCalls) = ReadCounters(run.SummaryJson);

            if (run.StartedAtUtc >= midnightUtc)
            {
                pagedCallsToday += pagedCalls;
            }

            if (apexCalls > 0 && (lastApexRunUtc is null || run.StartedAtUtc > lastApexRunUtc))
            {
                lastApexRunUtc = run.StartedAtUtc;
            }
        }

        return new LadderSyncRunLedger(pagedCallsToday, lastApexRunUtc);
    }

    public bool IsApexDue(TimeSpan interval, DateTime nowUtc)
        => interval <= TimeSpan.Zero
           || LastApexRunUtc is null
           || nowUtc - LastApexRunUtc.Value >= interval;

    /// <summary>
    /// The paginated calls this run may spend: the per-run cap, further bounded by what is left
    /// of the day when a daily ceiling is configured.
    /// </summary>
    public int RemainingBudget(LadderSyncOptions options)
    {
        var perRun = Math.Max(0, options.MaxRequestsPerRun);
        return options.MaxRequestsPerDay > 0
            ? Math.Min(perRun, Math.Max(0, options.MaxRequestsPerDay - PagedCallsToday))
            : perRun;
    }

    /// <summary>
    /// Reads the two counters the ledger needs out of a persisted summary. A summary that is not
    /// a <c>LadderSyncSummary</c> (a skip, a no-work row) simply has neither.
    /// </summary>
    private static (int PagedCalls, int ApexCalls) ReadCounters(string summaryJson)
    {
        try
        {
            using var document = JsonDocument.Parse(summaryJson);
            var root = document.RootElement;
            return (ReadInt(root, "pagedCalls"), ReadInt(root, "apexCalls"));
        }
        catch (JsonException)
        {
            return (0, 0);
        }

        static int ReadInt(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out var parsed)
                ? Math.Max(0, parsed)
                : 0;
    }
}
