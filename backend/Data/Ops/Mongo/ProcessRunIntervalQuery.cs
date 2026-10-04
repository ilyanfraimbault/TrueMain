using Data.Entities;
using Data.Logging.Mongo;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// The wall-clock interval of every recorded process run overlapping a window, for the
/// lane duty-cycle view of the admin Riot API panel (#1458). Returns raw intervals only:
/// clipping them to the window and taking their union is arithmetic, done by the Api.
/// </summary>
public interface IProcessRunIntervalQuery
{
    /// <summary>
    /// Runs started between <see cref="ProcessRunIntervalQuery.LookBehind"/> before <paramref name="sinceUtc"/>
    /// and <paramref name="untilUtc"/>, so a run that began just before the window and
    /// was still working inside it is counted. A run longer than the look-behind loses
    /// its part before the window start, which only matters for a window shorter than
    /// the run, and no pipeline process runs anywhere near a day.
    /// </summary>
    Task<IReadOnlyList<ProcessRunInterval>> GetAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken ct);
}

/// <summary>
/// One run's lane (the <c>jobMode</c> the host ran as, e.g. <c>FetchLane</c>), process and
/// interval. <see cref="EndUtc"/> is the recorded finish, or for a run still marked
/// Running its last heartbeat — the latest moment the run is known to have been alive.
/// </summary>
public sealed record ProcessRunInterval(string? Lane, string ProcessName, DateTime StartUtc, DateTime EndUtc);

public sealed class ProcessRunIntervalQuery(MongoLogContext context) : IProcessRunIntervalQuery
{
    /// <summary>How far before the window start a run may have begun and still be counted.</summary>
    public static readonly TimeSpan LookBehind = TimeSpan.FromDays(1);

    public async Task<IReadOnlyList<ProcessRunInterval>> GetAsync(
        DateTime sinceUtc,
        DateTime untilUtc,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        var filter = Builders<ProcessRunDocument>.Filter.Gte(doc => doc.StartedAtUtc, sinceUtc - LookBehind)
                     & Builders<ProcessRunDocument>.Filter.Lte(doc => doc.StartedAtUtc, untilUtc);

        // Projected to the five fields the arithmetic needs: a 30-day window is tens of
        // thousands of runs, and the summaries and errors would dominate the transfer.
        var rows = await context.ProcessRuns
            .Find(filter)
            .Project(doc => new
            {
                doc.JobMode,
                doc.ProcessName,
                doc.StartedAtUtc,
                doc.FinishedAtUtc,
                doc.Status,
                doc.LastHeartbeatAtUtc
            })
            .ToListAsync(ct);

        return rows
            .Select(row => new ProcessRunInterval(
                row.JobMode,
                row.ProcessName,
                row.StartedAtUtc,
                row.Status == ProcessRunStatus.Running
                    ? Max(row.FinishedAtUtc, row.LastHeartbeatAtUtc ?? row.StartedAtUtc)
                    : row.FinishedAtUtc))
            .ToList();
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;
}
