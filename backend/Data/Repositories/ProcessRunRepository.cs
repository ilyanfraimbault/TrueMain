using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class ProcessRunRepository(TrueMainDbContext db) : IProcessRunRepository
{
    public void Add(ProcessRun run)
        => db.ProcessRuns.Add(run);

    public async Task<ProcessRun?> GetByIdAsync(Guid id, CancellationToken ct)
        // Tracked: the caller mutates Status/FinishedAtUtc on the returned
        // instance and then SaveChanges to finalise the run.
        => await db.ProcessRuns.FindAsync([id], ct);

    public async Task<IReadOnlyList<ProcessRun>> GetRunningAsync(CancellationToken ct)
        // Tracked: the caller flips Status/FinishedAtUtc/Error on each returned
        // instance and then SaveChanges to abandon the orphaned runs.
        => await db.ProcessRuns
            .Where(run => run.Status == ProcessRunStatus.Running)
            .ToListAsync(ct);

    public async Task<int> TouchHeartbeatAsync(Guid id, DateTime nowUtc, CancellationToken ct)
        // Set-based UPDATE: no entity load, no SaveChanges. The Status guard makes
        // it a no-op for a missing or already-terminal row.
        => await db.ProcessRuns
            .Where(run => run.Id == id && run.Status == ProcessRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.LastHeartbeatAtUtc, nowUtc), ct);

    public async Task<DateTime?> GetLastCompletedRunStartAsync(string processName, CancellationToken ct)
        => await db.ProcessRuns
            .AsNoTracking()
            .Where(run => run.ProcessName == processName && run.Status != ProcessRunStatus.Running)
            .OrderByDescending(run => run.StartedAtUtc)
            .Select(run => (DateTime?)run.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
}
